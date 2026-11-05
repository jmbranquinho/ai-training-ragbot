// Mirrors python/tests/test_evaluation.py: same inputs, same expectations.
using RagBot.Core.Chat;
using RagBot.Core.Domain;
using RagBot.Core.Evaluation;

namespace RagBot.Tests;

public sealed class EvaluationTests
{
    [Fact]
    public void FirstHitRank_is_the_position_of_the_first_correct_page()
    {
        List<ScoredChunk> results = [Hit("https://x.no/a/"), Hit("https://x.no/b/"), Hit("https://x.no/c/")];

        Assert.Equal(2, RetrievalMetrics.FirstHitRank(results, ["https://x.no/c/", "https://x.no/b/"]));
        Assert.Null(RetrievalMetrics.FirstHitRank(results, ["https://x.no/z/"]));
    }

    [Fact]
    public void Summarize_computes_hit_at_k_and_mrr()
    {
        var summary = RetrievalMetrics.Summarize([1, 2, null, 4], [1, 3, 5]);

        Assert.Equal(new Dictionary<int, double> { [1] = 0.25, [3] = 0.5, [5] = 0.75 }, summary.HitAt);
        Assert.Equal((1 + (1.0 / 2) + 0 + (1.0 / 4)) / 4, summary.Mrr, precision: 12);
        Assert.Equal(1, summary.NotFound);
        Assert.Equal(4, summary.WorstRank);
    }

    [Fact]
    public void Judge_prompt_matches_the_shared_fixture()
    {
        var expected = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "testdata", "eval", "expected-judge-prompt.txt"));

        Assert.Equal(expected, ChatTestData.Render(JudgePrompt.BuildMessages("Spørsmål?", "Fasit.", "Svar [1].")));
    }

    [Theory]
    [InlineData("""{"grade": "correct", "reason": "ok"}""", Grade.Correct)]
    [InlineData("""{"grade": "partial", "reason": "ok"}""", Grade.Partial)]
    [InlineData("""{"grade": "great", "reason": "?"}""", Grade.Invalid)]
    [InlineData("not json", Grade.Invalid)]
    [InlineData("[1]", Grade.Invalid)]
    public void ParseVerdict(string reply, Grade grade)
    {
        Assert.Equal(grade, JudgePrompt.ParseVerdict(reply).Grade);
    }

    [Fact]
    public void The_real_question_set_is_well_formed()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "eval", "questions.json");
        var questions = QuestionSet.Load(path);

        Assert.Equal(questions.Count, questions.Select(q => q.Id).Distinct().Count()); // unique ids
        Assert.True(questions.Count(q => !q.Answerable) >= 3); // some "I don't know" questions
        Assert.All(questions.SelectMany(q => q.ExpectedUrls), u => Assert.StartsWith("https://www.stavanger.kommune.no/", u, StringComparison.Ordinal));
    }

    [Fact]
    public async Task EvaluateAnswers_grades_refusals_without_the_judge()
    {
        var aChunk = Hit("https://x.no/a/").Chunk;
        EvalQuestion[] questions =
        [
            new("q1", "no", "answered", ["https://x.no/a/"], "ref"),
            new("q2", "no", "refused", ["https://x.no/a/"], "ref"),
            new("q3", "no", "off-topic", [], "ref"),
        ];
        var generator = new ScriptedAnswerGenerator(new()
        {
            ["answered"] = new Answer("svar [1]", [aChunk]),
            ["refused"] = new Answer("Jeg vet ikke.", []),
            ["off-topic"] = new Answer("Jeg vet ikke.", []),
        });
        var judge = new FakeJudge();
        var chat = new ChatService(new FakeRetriever([Hit("https://x.no/a/")]), generator);

        var results = await EvaluationService.EvaluateAnswersAsync(chat, judge, questions);

        Assert.Equal(
            [("q1", Grade.Partial), ("q2", Grade.Incorrect), ("q3", Grade.Correct)], // judge / refused answerable / refused unanswerable
            results.Select(r => (r.QuestionId, r.Grade)));
        Assert.Equal(["answered"], judge.Calls); // the judge is only needed for real answers
        Assert.Equal([true, false, false], results.Select(r => r.CitedExpectedPage));
    }

    private static ScoredChunk Hit(string url)
    {
        return new ScoredChunk(new Chunk(url, new Uri(url), "T", "t", 0), 1);
    }
}
