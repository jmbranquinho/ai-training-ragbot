// Answer parsing: a pure function, no LLM call. Mirrors parse_answer in
// python/src/ragbot/chat/prompt.py.
using System.Text.Json;
using System.Text.RegularExpressions;
using RagBot.Core.Domain;

namespace RagBot.Core.Chat;

public static partial class AnswerParser
{
    /// <summary>
    /// Turn the model's JSON reply into an Answer, keeping only sources that really exist.
    /// - Sources are ordered by first citation in the text, then any extra ones from the JSON
    ///   "sources" list. Numbers outside 1..context.Count are dropped, duplicates removed.
    /// - [n] markers in the text are renumbered to match that order (the model numbers by context
    ///   position, e.g. [1][4], while the user sees a list numbered 1, 2, ...); markers pointing at
    ///   non-existent sources are removed.
    /// - An answer without any valid source counts as "unknown" (Answer.IsUnknown), so a made-up
    ///   citation can't make a hallucinated answer look supported.
    /// </summary>
    public static Answer Parse(string reply, IReadOnlyList<ScoredChunk> context)
    {
        ArgumentNullException.ThrowIfNull(reply);
        ArgumentNullException.ThrowIfNull(context);
        if (!TryReadReply(StripCodeFence(reply), out var text, out var numbers))
        {
            return new Answer(reply.Trim(), []); // not the JSON we asked for
        }

        var order = new List<int>(); // context numbers, in the order the user will see them
        foreach (var n in Citation().Matches(text).Select(m => int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)).Concat(numbers))
        {
            if (n >= 1 && n <= context.Count && !order.Contains(n))
            {
                order.Add(n);
            }
        }

        var renumbered = Citation().Replace(text, match =>
        {
            var position = order.IndexOf(int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
            return position >= 0 ? $"[{position + 1}]" : "";
        });
        return new Answer(renumbered.Trim(), order.Select(n => context[n - 1].Chunk).ToList());
    }

    private static bool TryReadReply(string json, out string text, out List<int> numbers)
    {
        text = "";
        numbers = [];
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("answer", out var answer)
                || answer.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            text = answer.GetString()!;
            if (!root.TryGetProperty("sources", out var sources))
            {
                return true; // missing list = no sources
            }

            if (sources.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var item in sources.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Number || !item.TryGetInt32(out var n))
                {
                    return false; // strict: only whole numbers
                }

                numbers.Add(n);
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Models sometimes wrap JSON in ```json ... ``` despite being asked not to.</summary>
    private static string StripCodeFence(string reply)
    {
        var match = CodeFence().Match(reply);
        return match.Success ? match.Groups[1].Value : reply;
    }

    [GeneratedRegex(@"\[(\d+)\]")]
    private static partial Regex Citation();

    [GeneratedRegex(@"^\s*```(?:json)?\s*(.*?)\s*```\s*$", RegexOptions.Singleline)]
    private static partial Regex CodeFence();
}
