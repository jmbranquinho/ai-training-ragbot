// Prompt building: a pure function, no LLM call. Mirrors build_messages in
// python/src/ragbot/chat/prompt.py. The exact text for the Stavanger site is pinned by
// testdata/chat/expected-prompt.txt, so both implementations send the model identical prompts.
// Other sites (see SiteProfile) fill the same template with their own wording.
using RagBot.Core.Domain;

namespace RagBot.Core.Chat;

public static class PromptBuilder
{
    // Why the "language" field: a rule like "answer in the question's language" alone was not
    // enough; with Norwegian sources the model often answered English questions in Norwegian.
    // Making it name the language first, before writing the answer, fixed that in testing.
    // The "don't know" rule deliberately has no Norwegian example ("Jeg vet ikke"): an earlier
    // version had one, and it made refusals to English questions come out in Norwegian.
    public static string SystemPromptFor(SiteProfile site)
    {
        ArgumentNullException.ThrowIfNull(site);

        // $$""" = interpolation with {{...}}, so the JSON braces below stay literal.
        return $$"""
            You are a helpful assistant for {{site.Subject}}.
            Answer the user's question using ONLY the numbered sources you are given.

            Rules:
            - {{site.LanguageRule}}
            - Be concise: a few sentences or a short list.
            - Every statement must come from the sources. Cite them with their numbers, like [1] or [2][3].
            - If the sources do not contain the answer, say that you don't know, in the language of the question, and {{site.FallbackAdvice}}. Do not guess and do not use outside knowledge.
            - Reply with JSON only, in this format:
              {"language": "<the language the question is written in>", "answer": "<your answer, written in that language, with [n] citations>", "sources": [<numbers of the sources you used>]}
              Use an empty sources list when you don't know.
            """;
    }

    /// <param name="question">The user's question.</param>
    /// <param name="context">Retrieved chunks, numbered [1], [2], … in this order.</param>
    /// <param name="site">Defaults to Stavanger, the site the shared fixtures pin.</param>
    public static IReadOnlyList<PromptMessage> BuildMessages(
        string question, IReadOnlyList<ScoredChunk> context, SiteProfile? site = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        var sources = string.Join(
            "\n\n",
            context.Select((hit, i) => $"[{i + 1}] (URL: {hit.Chunk.DocumentUrl})\n{hit.Chunk.Text}"));
        var user = $"Sources:\n\n{sources}\n\nQuestion: {question}";
        return
        [
            new PromptMessage("system", SystemPromptFor(site ?? SiteProfile.Stavanger)),
            new PromptMessage("user", user),
        ];
    }
}
