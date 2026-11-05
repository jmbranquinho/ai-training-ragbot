// IAnswerJudge backed by an Azure OpenAI chat deployment.
// Mirrors python/src/ragbot/evaluation/azure_judge.py.
using Azure.AI.OpenAI;
using OpenAI.Chat;

namespace RagBot.Core.Evaluation.Adapters;

public sealed class AzureOpenAIJudge(AzureOpenAIClient client, string deployment) : IAnswerJudge
{
    private static readonly ChatCompletionOptions Options = new()
    {
        Temperature = 0,
        ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat(),
    };

    private readonly ChatClient _chat = client.GetChatClient(deployment);

    public async Task<Verdict> JudgeAsync(string question, string reference, string answer, CancellationToken ct = default)
    {
        var messages = JudgePrompt.BuildMessages(question, reference, answer)
            .Select(m => m.Role == "system" ? (ChatMessage)new SystemChatMessage(m.Content) : new UserChatMessage(m.Content))
            .ToList();

        var completion = await _chat.CompleteChatAsync(messages, Options, ct).ConfigureAwait(false);
        return JudgePrompt.ParseVerdict(completion.Value.Content.Count > 0 ? completion.Value.Content[0].Text : "");
    }
}
