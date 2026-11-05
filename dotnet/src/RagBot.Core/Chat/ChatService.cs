// The chat use case: question → retrieve → answer with sources.
// Mirrors python/src/ragbot/chat/service.py.
//
// Two layers keep the bot from making things up:
// 1. If retrieval finds nothing, answer "I don't know" without calling the model at all.
// 2. Otherwise the model is told to answer only from the sources, and must cite them; an answer
//    without valid citations counts as unknown (see AnswerParser).
// There is deliberately no similarity threshold: relevant questions scored as low as 0.45
// (e.g. in English), so a fixed cut-off would reject good questions. Step 8 can measure it.
using RagBot.Core.Chat.Ports;
using RagBot.Core.Domain;

namespace RagBot.Core.Chat;

public sealed class ChatService(IRetriever retriever, IAnswerGenerator generator, int topK = 5)
{
    public const string NoContextAnswer = "Jeg vet ikke. Jeg fant ingen relevant informasjon om dette på nettsidene.";

    public async Task<Answer> AskAsync(string question, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(question);
        question = question.Trim();
        if (question.Length == 0)
        {
            throw new ArgumentException("question is empty", nameof(question));
        }

        var context = await retriever.RetrieveAsync(question, topK, ct).ConfigureAwait(false);
        if (context.Count == 0)
        {
            return new Answer(NoContextAnswer, []);
        }

        return await generator.AnswerAsync(question, context, ct).ConfigureAwait(false);
    }
}
