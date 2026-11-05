// Test double for IAnswerJudge. Mirrors FakeJudge in python/tests/test_evaluation.py.
using RagBot.Core.Evaluation;

namespace RagBot.Tests;

internal sealed class FakeJudge : IAnswerJudge
{
    public List<string> Calls { get; } = [];

    public Task<Verdict> JudgeAsync(string question, string reference, string answer, CancellationToken ct = default)
    {
        Calls.Add(question);
        return Task.FromResult(new Verdict(Grade.Partial, "judged"));
    }
}
