// Mirrors ChunkIndex in python/src/ragbot/indexing/ports.py.
// The contract shared by the offline pipeline (writes) and the chatbot (searches).
using RagBot.Core.Domain;

namespace RagBot.Core.Indexing.Ports;

public interface IChunkIndex
{
    Task AddAsync(
        IReadOnlyList<Chunk> chunks,
        IReadOnlyList<ReadOnlyMemory<float>> vectors,
        CancellationToken ct = default);

    Task<IReadOnlyList<ScoredChunk>> SearchAsync(
        ReadOnlyMemory<float> queryVector, int topK, CancellationToken ct = default);
}
