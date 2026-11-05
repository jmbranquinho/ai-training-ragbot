// LLM-as-judge prompt and parsing: pure functions. Mirrors python/src/ragbot/evaluation/judge.py.
// The prompt text is pinned by testdata/eval/expected-judge-prompt.txt.
// Limitation: the judge is the same model that wrote the answers, so it may be lenient with
// its own style. A stronger or different model as judge would be more trustworthy.
using System.Text.Json;
using RagBot.Core.Chat;

namespace RagBot.Core.Evaluation;

public static class JudgePrompt
{
    public const string SystemPrompt = """
        You grade answers from a chatbot for the website of Stavanger kommune.
        Compare the ANSWER with the REFERENCE, which holds the key facts from the website.
        The answer may be in a different language than the reference; that is fine.

        Grades:
        - correct: contains the key facts of the reference and nothing that contradicts it.
        - partial: misses some key facts, but contradicts nothing.
        - incorrect: contradicts the reference, misses the main point, or says it doesn't know.
        Only judge whether the key facts of the reference are present and not contradicted. Do NOT lower the grade because the answer contains more details than the reference: extra details are expected, since the answer comes from the full web page.

        Reply with JSON only: {"grade": "correct" | "partial" | "incorrect", "reason": "<one short sentence>"}
        """;

    public static IReadOnlyList<PromptMessage> BuildMessages(string question, string reference, string answer)
    {
        var user = $"QUESTION: {question}\n\nREFERENCE: {reference}\n\nANSWER: {answer}";
        return [new PromptMessage("system", SystemPrompt), new PromptMessage("user", user)];
    }

    public static Verdict ParseVerdict(string reply)
    {
        ArgumentNullException.ThrowIfNull(reply);
        try
        {
            using var document = JsonDocument.Parse(reply);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("grade", out var grade)
                && grade.ValueKind == JsonValueKind.String)
            {
                var reason = root.TryGetProperty("reason", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString()! : "";
                switch (grade.GetString())
                {
                    case "correct":
                        return new Verdict(Grade.Correct, reason);
                    case "partial":
                        return new Verdict(Grade.Partial, reason);
                    case "incorrect":
                        return new Verdict(Grade.Incorrect, reason);
                    default:
                        break;
                }
            }
        }
        catch (JsonException)
        {
            // falls through to Invalid
        }

        return new Verdict(Grade.Invalid, reply.Trim());
    }
}
