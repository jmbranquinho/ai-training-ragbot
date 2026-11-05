// Test double: returns a pre-set answer per question. Mirrors ScriptedGenerator in python/tests/test_evaluation.py.
using RagBot.Core.Chat.Ports;
using RagBot.Core.Domain;

namespace RagBot.Tests;

internal sealed class ScriptedAnswerGenerator(Dictionary<string, Answer> answers) : IAnswerGenerator
{
    public Task<Answer> AnswerAsync(string question, IReadOnlyList<ScoredChunk> context, CancellationToken ct = default)
    {
        return Task.FromResult(answers[question]);
    }
}
