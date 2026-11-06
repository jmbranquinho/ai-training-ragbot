// IAnswerGenerator backed by an Azure OpenAI chat deployment (e.g. gpt-4.1-mini).
// Mirrors python/src/ragbot/chat/adapters/azure_answer_generator.py. Thin on purpose: the prompt
// and the answer parsing live in PromptBuilder / AnswerParser, tested without calling the model.
using Azure.AI.OpenAI;
using OpenAI.Chat;
using RagBot.Core.Chat.Ports;
using RagBot.Core.Domain;

namespace RagBot.Core.Chat.Adapters;

public sealed class AzureOpenAIAnswerGenerator(AzureOpenAIClient client, string deployment, SiteProfile? site = null) : IAnswerGenerator
{
    private static readonly ChatCompletionOptions Options = new()
    {
        Temperature = 0, // as repeatable as possible
        ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat(), // guarantees syntactically valid JSON
    };

    private readonly ChatClient _chat = client.GetChatClient(deployment);

    public async Task<Answer> AnswerAsync(string question, IReadOnlyList<ScoredChunk> context, CancellationToken ct = default)
    {
        var messages = PromptBuilder.BuildMessages(question, context, site)
            .Select(m => m.Role == "system" ? (ChatMessage)new SystemChatMessage(m.Content) : new UserChatMessage(m.Content))
            .ToList();

        var completion = await _chat.CompleteChatAsync(messages, Options, ct).ConfigureAwait(false);
        var reply = completion.Value.Content.Count > 0 ? completion.Value.Content[0].Text : "";
        return AnswerParser.Parse(reply, context);
    }
}
