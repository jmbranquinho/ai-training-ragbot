// Integration tests against the real Postgres from docker-compose.yml.
// Mirrors python/tests/test_pgvector_index.py. [SkippableFact] + Skip.IfNot: skipped (not
// failed) when the database isn't running. They use their own schema, dropped afterwards.
using Npgsql;
using RagBot.Core;
using RagBot.Core.Domain;
using RagBot.Core.Indexing.Adapters;

namespace RagBot.Tests;

public sealed class PgVectorChunkIndexTests : IDisposable
{
    private const string Schema = "dotnet_test";
    private static readonly string ConnectionString = Settings.Load().PostgresConnectionString;
    private static readonly Lazy<bool> DatabaseAvailable = new(CanConnect);

    private static readonly Uri GlassUrl = new("https://x.no/glass/");
    private static readonly Uri PaintUrl = new("https://x.no/maling/");

    public void Dispose()
    {
        if (DatabaseAvailable.Value)
        {
            using var dataSource = NpgsqlDataSource.Create(ConnectionString);
            using var command = dataSource.CreateCommand($"DROP SCHEMA IF EXISTS {Schema} CASCADE");
            command.ExecuteNonQuery();
        }
    }

    [SkippableFact]
    public async Task Replace_search_and_delete()
    {
        Skip.IfNot(DatabaseAvailable.Value, "Postgres not running (docker compose up -d)");
        using var index = new PgVectorChunkIndex(ConnectionString, Schema, FakeEmbedder.Dimensions);
        await index.ClearAsync();
        Chunk[] glass = [MakeChunk(GlassUrl, 0, "glass og metall")];
        Chunk[] paint = [MakeChunk(PaintUrl, 0, "maling er farlig avfall"), MakeChunk(PaintUrl, 1, "lever maling på stasjonen")];
        await index.ReplaceDocumentAsync(GlassUrl, "v1", glass, Embed(glass));
        await index.ReplaceDocumentAsync(PaintUrl, "v1", paint, Embed(paint));

        var results = await index.SearchAsync(FakeEmbedder.EmbedWords("farlig maling"), topK: 2);

        Assert.All(results, r => Assert.Equal(PaintUrl, r.Chunk.DocumentUrl));
        Assert.Equal(paint[0] with { Metadata = null }, results[0].Chunk with { Metadata = null }); // records: value equality
        Assert.Equal("test", results[0].Chunk.Metadata!["chunker"]);
        Assert.True(results[1].Score > 0 && results[1].Score < results[0].Score && results[0].Score <= 1.0001);

        await index.ReplaceDocumentAsync(PaintUrl, "v2", paint[..1], Embed(paint[..1]));
        Assert.Equal("v2", (await index.DocumentVersionsAsync())[PaintUrl]);

        await index.DeleteDocumentAsync(GlassUrl);
        Assert.Equal([PaintUrl], (await index.DocumentVersionsAsync()).Keys);
    }

    [SkippableFact]
    public async Task Wrong_vector_size_asks_for_rebuild()
    {
        Skip.IfNot(DatabaseAvailable.Value, "Postgres not running (docker compose up -d)");
        using (var index = new PgVectorChunkIndex(ConnectionString, Schema, FakeEmbedder.Dimensions))
        {
            await index.ClearAsync(); // creates the table with Dimensions
        }

        using var other = new PgVectorChunkIndex(ConnectionString, Schema, FakeEmbedder.Dimensions * 2);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => other.DocumentVersionsAsync());
        Assert.Contains("--rebuild", error.Message, StringComparison.Ordinal);

        await other.ClearAsync(); // --rebuild path: recreates the table with the new size
        Assert.Empty(await other.DocumentVersionsAsync());
    }

    private static Chunk MakeChunk(Uri url, int position, string text)
    {
        return new Chunk($"{url}#{position}", url, "T", text, position, "H", new Dictionary<string, string> { ["chunker"] = "test" });
    }

    private static List<ReadOnlyMemory<float>> Embed(IEnumerable<Chunk> chunks)
    {
        return chunks.Select(c => FakeEmbedder.EmbedWords(c.Text)).ToList();
    }

    private static bool CanConnect()
    {
        try
        {
            using var connection = new NpgsqlConnection(ConnectionString + ";Timeout=2");
            connection.Open();
            return true;
        }
        catch (NpgsqlException)
        {
            return false;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }
}
