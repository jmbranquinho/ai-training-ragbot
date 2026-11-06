// Prompt building: a pure function, no LLM call. Mirrors build_messages in
// python/src/ragbot/chat/prompt.py. The exact text is pinned by testdata/chat/expected-prompt.txt,
// so both implementations send the model identical prompts.
using RagBot.Core.Domain;

namespace RagBot.Core.Chat;

public static class PromptBuilder
{
    public const string SystemPrompt = """
        You are a helpful assistant for the website of Stavanger kommune, a Norwegian municipality.
        Answer the user's question using ONLY the numbered sources you are given.

        Rules:
        - Answer in the language of the question, even though the sources are in Norwegian (an English question gets an English answer).
        - Be concise: a few sentences or a short list.
        - Every statement must come from the sources. Cite them with their numbers, like [1] or [2][3].
        - If the sources do not contain the answer, say that you don't know, in the language of the question, and suggest contacting Stavanger kommune. Do not guess and do not use outside knowledge.
        - Reply with JSON only, in this format:
          {"language": "<the language the question is written in>", "answer": "<your answer, written in that language, with [n] citations>", "sources": [<numbers of the sources you used>]}
          Use an empty sources list when you don't know.
        """;

    // Why the "language" field: a rule like "answer in the question's language" alone was not
    // enough; with Norwegian sources the model often answered English questions in Norwegian.
    // Making it name the language first, before writing the answer, fixed that in testing.
    // The "don't know" rule deliberately has no Norwegian example ("Jeg vet ikke"): an earlier
    // version had one, and it made refusals to English questions come out in Norwegian.

    public static IReadOnlyList<PromptMessage> BuildMessages(string question, IReadOnlyList<ScoredChunk> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var sources = string.Join(
            "\n\n",
            context.Select((hit, i) => $"[{i + 1}] (URL: {hit.Chunk.DocumentUrl})\n{hit.Chunk.Text}"));
        var user = $"Sources:\n\n{sources}\n\nQuestion: {question}";
        return [new PromptMessage("system", SystemPrompt), new PromptMessage("user", user)];
    }
}
