// Mirrors ChunkIndex in python/src/ragbot/indexing/ports.py.
// The contract shared by the offline pipeline (writes) and the chatbot (searches).
using RagBot.Core.Domain;

namespace RagBot.Core.Indexing.Ports;

public interface IChunkIndex
{
    /// <summary>Version of every indexed document, by URL. Lets indexing skip unchanged pages.</summary>
    Task<IReadOnlyDictionary<Uri, string>> DocumentVersionsAsync(CancellationToken ct = default);

    /// <summary>Replace all chunks of one document (its chunk count may have changed).</summary>
    Task ReplaceDocumentAsync(
        Uri documentUrl,
        string version,
        IReadOnlyList<Chunk> chunks,
        IReadOnlyList<ReadOnlyMemory<float>> vectors,
        CancellationToken ct = default);

    Task DeleteDocumentAsync(Uri documentUrl, CancellationToken ct = default);

    /// <summary>Remove everything, e.g. after changing the embedding size.</summary>
    Task ClearAsync(CancellationToken ct = default);

    /// <summary>The topK chunks most similar to the query vector, best first (cosine similarity).</summary>
    Task<IReadOnlyList<ScoredChunk>> SearchAsync(
        ReadOnlyMemory<float> queryVector, int topK, CancellationToken ct = default);

    /// <summary>
    /// The topK chunks matching the query's words, best first. Scores are on a different scale
    /// than SearchAsync, so only compare ranks across the two.
    /// </summary>
    Task<IReadOnlyList<ScoredChunk>> KeywordSearchAsync(string query, int topK, CancellationToken ct = default);
}
