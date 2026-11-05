// Mirrors AnswerGenerator in python/src/ragbot/chat/ports.py.
// The LLM call lives behind this interface; the Azure OpenAI client will be an adapter (Step 7).
using RagBot.Core.Domain;

namespace RagBot.Core.Chat.Ports;

public interface IAnswerGenerator
{
    Task<Answer> AnswerAsync(
        string question, IReadOnlyList<ScoredChunk> context, CancellationToken ct = default);
}
