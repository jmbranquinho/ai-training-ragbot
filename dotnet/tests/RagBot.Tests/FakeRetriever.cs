// Test double for IRetriever. Mirrors FakeRetriever in python/tests/test_chat.py.
using RagBot.Core.Chat.Ports;
using RagBot.Core.Domain;

namespace RagBot.Tests;

internal sealed class FakeRetriever(IReadOnlyList<ScoredChunk> results) : IRetriever
{
    public List<(string Question, int TopK)> Calls { get; } = [];

    public Task<IReadOnlyList<ScoredChunk>> RetrieveAsync(string question, int topK, CancellationToken ct = default)
    {
        Calls.Add((question, topK));
        IReadOnlyList<ScoredChunk> hits = results.Take(topK).ToList();
        return Task.FromResult(hits);
    }
}
