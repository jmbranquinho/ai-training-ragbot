// Mirrors Embedder in python/src/ragbot/indexing/ports.py.
namespace RagBot.Core.Indexing.Ports;

public interface IEmbedder
{
    /// <summary>One vector per text, in the same order.</summary>
    Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedAsync(
        IReadOnlyList<string> texts, CancellationToken ct = default);
}
