// The fixed example behind testdata/chat/expected-prompt.txt.
// Mirrors python/tests/chat_fixtures.py (same values).
using RagBot.Core.Chat;
using RagBot.Core.Domain;

namespace RagBot.Tests;

internal static class ChatTestData
{
    public const string Question = "Hvor leverer jeg maling?";

    public static readonly IReadOnlyList<ScoredChunk> Context =
    [
        new(new Chunk(
            "https://x.no/farlig-avfall/#0",
            new Uri("https://x.no/farlig-avfall/"),
            "Farlig avfall",
            "Farlig avfall > Levering\n\nLever maling på gjenvinningsstasjonen.",
            0,
            "Levering"), 0.9),
        new(new Chunk(
            "https://x.no/glass/#2",
            new Uri("https://x.no/glass/"),
            "Glass",
            "Glass > Henting\n\nGlass hentes hver fjerde uke.",
            2,
            "Henting"), 0.5),
    ];

    /// <summary>Messages as plain text, the format of expected-prompt.txt.</summary>
    public static string Render(IEnumerable<PromptMessage> messages)
    {
        return string.Concat(messages.Select(m => $"=== {m.Role} ===\n{m.Content}\n"));
    }
}
