// Everything that differs between the websites RagBot can be pointed at: the wording of the
// prompts, the full-text search language, and where data, index and eval questions live.
// The use cases stay site-neutral; Program.cs picks a profile with --site and passes its values
// on. Which adapters crawl the site (site map vs MediaWiki page list, HTML parser) is also
// chosen in Program.cs, from Key.
//
// The Python version only knows Stavanger. The Stavanger profile produces exactly the same
// prompts as before, so the shared fixtures in testdata/ still pin both implementations.
using RagBot.Core.Chat;

namespace RagBot.Core;

/// <param name="Key">Name used with --site.</param>
/// <param name="BaseUrl">Root of the website; robots.txt is read from here.</param>
/// <param name="Subject">Completes "You are a helpful assistant for ...".</param>
/// <param name="LanguageRule">The prompt rule about the answer's language.</param>
/// <param name="FallbackAdvice">What a "don't know" answer should suggest.</param>
/// <param name="JudgeSubject">Completes "You grade answers from a chatbot for ...".</param>
/// <param name="NoContextAnswer">Reply when retrieval finds nothing (the model is not called).</param>
/// <param name="TextSearchConfig">PostgreSQL text search configuration (stemming + stopwords).</param>
/// <param name="DefaultSchema">Database schema for this site's index.</param>
/// <param name="DataDir">Folder for the stored pages, relative to dotnet/.</param>
/// <param name="QuestionsFile">Evaluation questions, relative to the repo root.</param>
/// <param name="Greeting">First line printed by the chat command.</param>
/// <param name="SourcesLabel">Heading of the source list under an answer.</param>
/// <param name="DefaultMode">Retrieval mode when --mode is not given (chosen by eval, see eval/RESULTS.md).</param>
public sealed record SiteProfile(
    string Key,
    Uri BaseUrl,
    string Subject,
    string LanguageRule,
    string FallbackAdvice,
    string JudgeSubject,
    string NoContextAnswer,
    string TextSearchConfig,
    string DefaultSchema,
    string DataDir,
    string QuestionsFile,
    string Greeting,
    string SourcesLabel,
    string DefaultMode)
{
    public static SiteProfile Stavanger { get; } = new(
        "stavanger",
        new Uri("https://www.stavanger.kommune.no"),
        "the website of Stavanger kommune, a Norwegian municipality",
        "Answer in the language of the question, even though the sources are in Norwegian (an English question gets an English answer).",
        "suggest contacting Stavanger kommune",
        "the website of Stavanger kommune",
        ChatService.NoContextAnswer,
        "norwegian",
        "dotnet",
        Path.Combine("data", "raw"),
        Path.Combine("eval", "questions.json"),
        "Spør om Stavanger kommune / Ask about Stavanger kommune.\nTom linje avslutter / An empty line quits.",
        "Kilder:",
        "hybrid");

    public static SiteProfile Satisfactory { get; } = new(
        "satisfactory",
        new Uri("https://satisfactory.wiki.gg"),
        "the Satisfactory Wiki, a community wiki about the factory-building game Satisfactory",
        "Answer in the language of the question (a Norwegian question gets a Norwegian answer).",
        "suggest looking it up on the Satisfactory Wiki directly",
        "the Satisfactory Wiki",
        "I don't know. I found no relevant information about this on the Satisfactory Wiki.",
        "english",
        "dotnet_satisfactory",
        Path.Combine("data", "satisfactory"),
        Path.Combine("eval", "questions-satisfactory.json"),
        "Ask about the game Satisfactory (answers come from satisfactory.wiki.gg).\nAn empty line quits.",
        "Sources:",
        "vector"); // keyword search pulls in Trivia/History sections that repeat the words

    public static IReadOnlyList<SiteProfile> All { get; } = [Stavanger, Satisfactory];

    public static SiteProfile Get(string key)
    {
        return All.FirstOrDefault(s => s.Key == key)
            ?? throw new ArgumentException(
                $"unknown site '{key}' (known: {string.Join(", ", All.Select(s => s.Key))})", nameof(key));
    }
}
