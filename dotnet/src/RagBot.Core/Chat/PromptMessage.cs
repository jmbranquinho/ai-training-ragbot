// One chat message (role = "system" or "user"), independent of any LLM SDK's types.
namespace RagBot.Core.Chat;

public sealed record PromptMessage(string Role, string Content);
