// Interfaces for the indexing feature. Mirrors python/src/ragbot/indexing/ports.py.
// IChunkIndex is the contract shared by the offline pipeline (writes) and the chatbot (searches).
using RagBot.Core.Domain;

namespace RagBot.Core.Indexing;

public interface IChunker
{
    IReadOnlyList<Chunk> Chunk(Document document);
}

public interface IEmbedder
{
    Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedAsync(
        IReadOnlyList<string> texts, CancellationToken ct = default);
}

public interface IChunkIndex
{
    Task AddAsync(
        IReadOnlyList<Chunk> chunks,
        IReadOnlyList<ReadOnlyMemory<float>> vectors,
        CancellationToken ct = default);

    Task<IReadOnlyList<ScoredChunk>> SearchAsync(
        ReadOnlyMemory<float> queryVector, int topK, CancellationToken ct = default);
}
