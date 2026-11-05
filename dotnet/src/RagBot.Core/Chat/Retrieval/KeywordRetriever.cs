// Full-text search: exact words, but no understanding of meaning.
// Mirrors python/src/ragbot/chat/retrieval.py.
using RagBot.Core.Chat.Ports;
using RagBot.Core.Domain;
using RagBot.Core.Indexing.Ports;

namespace RagBot.Core.Chat.Retrieval;

public sealed class KeywordRetriever(IChunkIndex index) : IRetriever
{
    public Task<IReadOnlyList<ScoredChunk>> RetrieveAsync(string question, int topK, CancellationToken ct = default)
    {
        return index.KeywordSearchAsync(question, topK, ct);
    }
}
