// Interfaces for the chat feature. Mirrors python/src/ragbot/chat/ports.py.
// The LLM call lives behind IAnswerGenerator; the Azure OpenAI client will be an adapter (Step 7).
using RagBot.Core.Domain;

namespace RagBot.Core.Chat;

public interface IRetriever
{
    Task<IReadOnlyList<ScoredChunk>> RetrieveAsync(
        string question, int topK, CancellationToken ct = default);
}

public interface IAnswerGenerator
{
    Task<Answer> AnswerAsync(
        string question, IReadOnlyList<ScoredChunk> context, CancellationToken ct = default);
}
