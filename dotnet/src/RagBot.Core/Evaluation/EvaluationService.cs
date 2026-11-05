// Evaluation use cases: run the question set through a retriever, or through the whole chatbot.
// Mirrors python/src/ragbot/evaluation/service.py.
using RagBot.Core.Chat;
using RagBot.Core.Chat.Ports;

namespace RagBot.Core.Evaluation;

public static class EvaluationService
{
    public static readonly IReadOnlyList<int> Ks = [1, 3, 5, 10];

    /// <summary>Only answerable questions: for the others there is no "right page" to find.</summary>
    public static async Task<(IReadOnlyList<RetrievalResult> Results, RetrievalSummary Summary)> EvaluateRetrievalAsync(
        IRetriever retriever, IEnumerable<EvalQuestion> questions, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(retriever);
        var results = new List<RetrievalResult>();
        foreach (var q in questions.Where(q => q.Answerable))
        {
            var hits = await retriever.RetrieveAsync(q.Question, Ks.Max(), ct).ConfigureAwait(false);
            results.Add(new RetrievalResult(q.Id, RetrievalMetrics.FirstHitRank(hits, q.ExpectedUrls.ToList())));
        }

        return (results, RetrievalMetrics.Summarize(results.Select(r => r.Rank).ToList(), Ks));
    }

    public static async Task<IReadOnlyList<AnswerResult>> EvaluateAnswersAsync(
        ChatService chat, IAnswerJudge judge, IEnumerable<EvalQuestion> questions, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(chat);
        ArgumentNullException.ThrowIfNull(judge);
        var results = new List<AnswerResult>();
        foreach (var q in questions)
        {
            var answer = await chat.AskAsync(q.Question, ct).ConfigureAwait(false);
            var cited = answer.Sources.Any(s => q.ExpectedUrls.Contains(s.DocumentUrl.ToString()));

            Verdict verdict;
            if (!q.Answerable) // the right answer is "I don't know": no judge needed
            {
                verdict = answer.IsUnknown
                    ? new Verdict(Grade.Correct, "refused as expected")
                    : new Verdict(Grade.Incorrect, "answered an unanswerable question");
            }
            else if (answer.IsUnknown)
            {
                verdict = new Verdict(Grade.Incorrect, "said it didn't know");
            }
            else
            {
                verdict = await judge.JudgeAsync(q.Question, q.Reference, answer.Text, ct).ConfigureAwait(false);
            }

            results.Add(new AnswerResult(q.Id, q.Answerable, answer.Text, answer.IsUnknown, cited, verdict.Grade, verdict.Reason));
        }

        return results;
    }
}
