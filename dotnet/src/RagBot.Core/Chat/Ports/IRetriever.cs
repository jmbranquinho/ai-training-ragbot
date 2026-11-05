// Mirrors Retriever in python/src/ragbot/chat/ports.py.
using RagBot.Core.Domain;

namespace RagBot.Core.Chat.Ports;

public interface IRetriever
{
    Task<IReadOnlyList<ScoredChunk>> RetrieveAsync(
        string question, int topK, CancellationToken ct = default);
}
