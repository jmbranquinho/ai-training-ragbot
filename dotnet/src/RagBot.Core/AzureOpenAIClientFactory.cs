// Creates the Azure OpenAI client shared by the embedder and the answer generator.
// Mirrors python/src/ragbot/azure_openai.py.
using System.ClientModel.Primitives;
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

        // Bulk indexing (thousands of chunks) hits the deployment's per-minute quota (HTTP 429).
        // The default 3 retries give up within seconds; with 10, the exponential backoff (which
        // honours the server's Retry-After hint) can wait out a full quota minute.
        var options = new AzureOpenAIClientOptions { RetryPolicy = new ClientRetryPolicy(maxRetries: 10) };
        return apiKey is not null
            ? new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey), options)
            : new AzureOpenAIClient(new Uri(endpoint), new DefaultAzureCredential(), options);
    }
}
