// IChunkIndex kept in memory: brute-force cosine similarity. For tests and experiments.
// Mirrors python/src/ragbot/indexing/adapters/in_memory_index.py.
using System.Numerics.Tensors;
using System.Text.RegularExpressions;
using RagBot.Core.Domain;
using RagBot.Core.Indexing.Ports;

namespace RagBot.Core.Indexing.Adapters;

public sealed partial class InMemoryChunkIndex : IChunkIndex
{
    private readonly Dictionary<Uri, (string Version, List<(Chunk Chunk, ReadOnlyMemory<float> Vector)> Entries)> _documents = [];

    public Task<IReadOnlyDictionary<Uri, string>> DocumentVersionsAsync(CancellationToken ct = default)
    {
        IReadOnlyDictionary<Uri, string> versions = _documents.ToDictionary(d => d.Key, d => d.Value.Version);
        return Task.FromResult(versions);
    }

    public Task ReplaceDocumentAsync(
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

        _documents[documentUrl] = (version, chunks.Zip(vectors).ToList());
        return Task.CompletedTask;
    }

    public Task DeleteDocumentAsync(Uri documentUrl, CancellationToken ct = default)
    {
        _documents.Remove(documentUrl);
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken ct = default)
    {
        _documents.Clear();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ScoredChunk>> SearchAsync(
        ReadOnlyMemory<float> queryVector, int topK, CancellationToken ct = default)
    {
        IReadOnlyList<ScoredChunk> results = _documents.Values
            .SelectMany(d => d.Entries)
            .Select(e => new ScoredChunk(e.Chunk, TensorPrimitives.CosineSimilarity(queryVector.Span, e.Vector.Span)))
            .OrderByDescending(s => s.Score)
            .Take(topK)
            .ToList();
        return Task.FromResult(results);
    }

    /// <summary>Simple stand-in for full-text search: score = number of query words in the chunk.</summary>
    public Task<IReadOnlyList<ScoredChunk>> KeywordSearchAsync(string query, int topK, CancellationToken ct = default)
    {
        var words = Words(query);
        IReadOnlyList<ScoredChunk> results = _documents.Values
            .SelectMany(d => d.Entries)
            .Select(e => new ScoredChunk(e.Chunk, words.Intersect(Words(e.Chunk.Text)).Count()))
            .Where(s => s.Score > 0)
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.Chunk.Id, StringComparer.Ordinal)
            .Take(topK)
            .ToList();
        return Task.FromResult(results);
    }

    private static HashSet<string> Words(string text)
    {
        return WordPattern().Matches(text.ToLowerInvariant()).Select(m => m.Value).ToHashSet();
    }

    [GeneratedRegex(@"\w+")]
    private static partial Regex WordPattern();
}
