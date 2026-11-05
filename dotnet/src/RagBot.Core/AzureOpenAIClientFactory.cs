// Creates the Azure OpenAI client shared by the embedder and the answer generator.
// Mirrors python/src/ragbot/azure_openai.py.
using Azure;
using Azure.AI.OpenAI;
using Azure.Identity;

namespace RagBot.Core;

public static class AzureOpenAIClientFactory
{
    /// <summary>API key if given, otherwise Entra ID (your az login locally, managed identity in Azure).</summary>
    public static AzureOpenAIClient Create(string endpoint, string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not set (see .env.example)");
        }

        return apiKey is not null
            ? new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey))
            : new AzureOpenAIClient(new Uri(endpoint), new DefaultAzureCredential());
    }
}
