// IChunkIndex in PostgreSQL + pgvector. One `chunks` table per schema (python / dotnet).
// Mirrors python/src/ragbot/indexing/adapters/pgvector_index.py; the table layout is identical:
//   id, document_url, document_version, document_title, heading, position, text, metadata,
//   embedding vector(N)  -- searched with cosine distance through an HNSW index
//   tsv tsvector         -- generated full-text column (Norwegian or English stemming), for hybrid search
//
// Vectors are sent as float4[] and cast to vector in SQL (@q::vector), so no extra pgvector
// .NET package is needed.
using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;
using NpgsqlTypes;
using RagBot.Core.Domain;
using RagBot.Core.Indexing.Ports;

namespace RagBot.Core.Indexing.Adapters;

public sealed partial class PgVectorChunkIndex : IChunkIndex, IDisposable
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly string _schema;
    private readonly int _dimensions;
    private readonly string _textSearchConfig;
    private bool _ready; // table is created/checked on first use, so ClearAsync can run first

    /// <param name="connectionString">Npgsql connection string.</param>
    /// <param name="schema">Schema holding the chunks table, e.g. "dotnet".</param>
    /// <param name="dimensions">Embedding size.</param>
    /// <param name="textSearchConfig">
    /// PostgreSQL text search configuration for keyword search, e.g. "norwegian" or "english".
    /// It is baked into the generated tsv column, so changing it needs --rebuild.
    /// </param>
    public PgVectorChunkIndex(string connectionString, string schema, int dimensions, string textSearchConfig = "norwegian")
    {
        if (!SafeIdentifier().IsMatch(schema))
        {
            throw new ArgumentException($"invalid schema name: {schema}", nameof(schema));
        }

        if (!SafeIdentifier().IsMatch(textSearchConfig))
        {
            throw new ArgumentException($"invalid text search configuration: {textSearchConfig}", nameof(textSearchConfig));
        }

        _dataSource = NpgsqlDataSource.Create(connectionString);
        _schema = schema;
        _dimensions = dimensions;
        _textSearchConfig = textSearchConfig;
    }

    public void Dispose()
    {
        _dataSource.Dispose();
    }

    public async Task<IReadOnlyDictionary<Uri, string>> DocumentVersionsAsync(CancellationToken ct = default)
    {
        await EnsureTableAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(
            $"SELECT DISTINCT document_url, document_version FROM {_schema}.chunks");
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);

        var versions = new Dictionary<Uri, string>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            versions[new Uri(reader.GetString(0))] = reader.GetString(1);
        }

        return versions;
    }

    public async Task ReplaceDocumentAsync(
        Uri documentUrl,
        string version,
        IReadOnlyList<Chunk> chunks,
        IReadOnlyList<ReadOnlyMemory<float>> vectors,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        ArgumentNullException.ThrowIfNull(vectors);
        if (chunks.Count != vectors.Count)
        {
            throw new ArgumentException("need exactly one vector per chunk", nameof(vectors));
        }

        await EnsureTableAsync(ct).ConfigureAwait(false);
        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        // One round trip for the delete + all inserts; the transaction makes it atomic.
        await using var batch = new NpgsqlBatch(connection, transaction);
        var delete = new NpgsqlBatchCommand($"DELETE FROM {_schema}.chunks WHERE document_url = @url");
        delete.Parameters.AddWithValue("url", documentUrl.ToString());
        batch.BatchCommands.Add(delete);

        foreach (var (chunk, vector) in chunks.Zip(vectors))
        {
            var insert = new NpgsqlBatchCommand(
                $"INSERT INTO {_schema}.chunks (id, document_url, document_version, document_title, heading,"
                + " position, text, metadata, embedding)"
                + " VALUES (@id, @url, @version, @title, @heading, @position, @text, @metadata, @embedding::vector)");
            insert.Parameters.AddWithValue("id", chunk.Id);
            insert.Parameters.AddWithValue("url", documentUrl.ToString());
            insert.Parameters.AddWithValue("version", version);
            insert.Parameters.AddWithValue("title", chunk.DocumentTitle);
            insert.Parameters.AddWithValue("heading", (object?)chunk.Heading ?? DBNull.Value);
            insert.Parameters.AddWithValue("position", chunk.Position);
            insert.Parameters.AddWithValue("text", chunk.Text);
            insert.Parameters.AddWithValue("metadata", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(chunk.Metadata ?? new Dictionary<string, string>()));
            insert.Parameters.AddWithValue("embedding", vector.ToArray());
            batch.BatchCommands.Add(insert);
        }

        await batch.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteDocumentAsync(Uri documentUrl, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(documentUrl);
        await EnsureTableAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand($"DELETE FROM {_schema}.chunks WHERE document_url = @url");
        command.Parameters.AddWithValue("url", documentUrl.ToString());
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Drop and recreate the table (also picks up a changed embedding size).</summary>
    public async Task ClearAsync(CancellationToken ct = default)
    {
        await using (var command = _dataSource.CreateCommand($"DROP TABLE IF EXISTS {_schema}.chunks"))
        {
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        _ready = false;
        await EnsureTableAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ScoredChunk>> SearchAsync(
        ReadOnlyMemory<float> queryVector, int topK, CancellationToken ct = default)
    {
        await EnsureTableAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(
            "SELECT id, document_url, document_title, text, position, heading, metadata::text,"
            + " 1 - (embedding <=> @q::vector) AS score" // cosine distance → similarity
            + $" FROM {_schema}.chunks ORDER BY embedding <=> @q::vector LIMIT @k");
        command.Parameters.AddWithValue("q", queryVector.ToArray());
        command.Parameters.AddWithValue("k", topK);
        return await ReadScoredChunksAsync(command, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Full-text search (stemming + stopwords in the configured language) on the generated tsv column.
    /// plainto_tsquery joins words with AND (&amp;), so a whole question would only match chunks
    /// containing every word. Swapping &amp; for OR (|) means "any word matches, more matches rank
    /// higher", which suits natural-language questions.
    /// </summary>
    public async Task<IReadOnlyList<ScoredChunk>> KeywordSearchAsync(string query, int topK, CancellationToken ct = default)
    {
        await EnsureTableAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(
            "SELECT id, document_url, document_title, text, position, heading, metadata::text,"
            + " ts_rank_cd(tsv, q.query)::float8 AS score"
            + $" FROM {_schema}.chunks, (SELECT replace(plainto_tsquery('{_textSearchConfig}', @q)::text,"
            + " '&', '|')::tsquery AS query) AS q"
            + " WHERE tsv @@ q.query ORDER BY score DESC, id LIMIT @k");
        command.Parameters.AddWithValue("q", query);
        command.Parameters.AddWithValue("k", topK);
        return await ReadScoredChunksAsync(command, ct).ConfigureAwait(false);
    }

    /// <summary>Columns: id, document_url, document_title, text, position, heading, metadata, score.</summary>
    private static async Task<IReadOnlyList<ScoredChunk>> ReadScoredChunksAsync(NpgsqlCommand command, CancellationToken ct)
    {
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var results = new List<ScoredChunk>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var chunk = new Chunk(
                reader.GetString(0),
                new Uri(reader.GetString(1)),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt32(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(6)));
            results.Add(new ScoredChunk(chunk, reader.GetDouble(7)));
        }

        return results;
    }

    private async Task EnsureTableAsync(CancellationToken ct)
    {
        if (_ready)
        {
            return;
        }

        var create = $"""
            CREATE EXTENSION IF NOT EXISTS vector;
            CREATE SCHEMA IF NOT EXISTS {_schema};
            CREATE TABLE IF NOT EXISTS {_schema}.chunks (
                id               text PRIMARY KEY,
                document_url     text NOT NULL,
                document_version text NOT NULL,
                document_title   text NOT NULL,
                heading          text,
                position         integer NOT NULL,
                text             text NOT NULL,
                metadata         jsonb NOT NULL DEFAULT '{"{}"}'::jsonb,
                embedding        vector({_dimensions}) NOT NULL,
                tsv              tsvector GENERATED ALWAYS AS (to_tsvector('{_textSearchConfig}', text)) STORED
            );
            CREATE INDEX IF NOT EXISTS chunks_document_url ON {_schema}.chunks (document_url);
            CREATE INDEX IF NOT EXISTS chunks_embedding ON {_schema}.chunks
                USING hnsw (embedding vector_cosine_ops);
            CREATE INDEX IF NOT EXISTS chunks_tsv ON {_schema}.chunks USING gin (tsv);
            """;
        await using (var command = _dataSource.CreateCommand(create))
        {
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await using (var check = _dataSource.CreateCommand(
            "SELECT atttypmod FROM pg_attribute WHERE attrelid = @table::regclass AND attname = 'embedding'"))
        {
            check.Parameters.AddWithValue("table", $"{_schema}.chunks");
            var existing = (int?)await check.ExecuteScalarAsync(ct).ConfigureAwait(false);
            if (existing is not null && existing != _dimensions)
            {
                throw new InvalidOperationException(
                    $"{_schema}.chunks stores {existing}-dimensional vectors, but EMBEDDING_DIMENSIONS is "
                    + $"{_dimensions}. Re-run indexing with --rebuild.");
            }
        }

        // The language is part of the generated column; querying a Norwegian column with
        // English stemming would silently miss most matches.
        await using (var check = _dataSource.CreateCommand(
            "SELECT pg_get_expr(d.adbin, d.adrelid) FROM pg_attrdef d"
            + " JOIN pg_attribute a ON a.attrelid = d.adrelid AND a.attnum = d.adnum"
            + " WHERE d.adrelid = @table::regclass AND a.attname = 'tsv'"))
        {
            check.Parameters.AddWithValue("table", $"{_schema}.chunks");
            var expression = (string?)await check.ExecuteScalarAsync(ct).ConfigureAwait(false);
            if (expression is not null && !expression.Contains($"'{_textSearchConfig}'", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{_schema}.chunks was built for another search language ({expression}), not "
                    + $"'{_textSearchConfig}'. Re-run indexing with --rebuild.");
            }
        }

        _ready = true;
    }

    // Schema names are put into SQL text (identifiers can't be parameters), so only allow safe ones.
    [GeneratedRegex("^[a-z_][a-z0-9_]*$")]
    private static partial Regex SafeIdentifier();
}
