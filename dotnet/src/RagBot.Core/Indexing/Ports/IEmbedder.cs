// Mirrors Embedder in python/src/ragbot/indexing/ports.py.
namespace RagBot.Core.Indexing.Ports;

public interface IEmbedder
{
    Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedAsync(
        IReadOnlyList<string> texts, CancellationToken ct = default);
}
