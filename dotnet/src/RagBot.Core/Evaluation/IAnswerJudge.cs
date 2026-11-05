// Mirrors AnswerJudge in python/src/ragbot/evaluation/judge.py.
namespace RagBot.Core.Evaluation;

public interface IAnswerJudge
{
    Task<Verdict> JudgeAsync(string question, string reference, string answer, CancellationToken ct = default);
}
