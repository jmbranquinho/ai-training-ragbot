// Test double for IAnswerGenerator. Mirrors FakeGenerator in python/tests/test_chat.py.
using RagBot.Core.Chat.Ports;
using RagBot.Core.Domain;

namespace RagBot.Tests;

internal sealed class FakeAnswerGenerator : IAnswerGenerator
{
    public List<IReadOnlyList<ScoredChunk>> Contexts { get; } = [];

    public Task<Answer> AnswerAsync(string question, IReadOnlyList<ScoredChunk> context, CancellationToken ct = default)
    {
        Contexts.Add(context);
        return Task.FromResult(new Answer("svar", [context[0].Chunk]));
    }
}
