// Settings from environment variables and the repo's .env file.
// Mirrors python/src/ragbot/config.py. Only the composition root (Program.cs) and integration
// tests read settings; everything else gets plain values passed in.
namespace RagBot.Core;

/// <param name="AzureOpenAIApiKey">Null → sign in with Entra ID (az login).</param>
/// <param name="AzureOpenAIEndpoint">Endpoint of the Azure OpenAI / AI Foundry resource.</param>
/// <param name="EmbeddingDeployment">Name of the embedding model deployment.</param>
/// <param name="EmbeddingDimensions">Vector size requested from the model.</param>
/// <param name="ChatDeployment">Name of the chat model deployment.</param>
/// <param name="PostgresConnectionString">Npgsql connection string.</param>
public sealed record Settings(
    string AzureOpenAIEndpoint,
    string? AzureOpenAIApiKey,
    string EmbeddingDeployment,
    int EmbeddingDimensions,
    string ChatDeployment,
    string PostgresConnectionString)
{
    public static Settings Load()
    {
        // TraversePath: look for .env in the current folder and its parents (≈ find_dotenv).
        // NoClobber: real environment variables win over the file, like python-dotenv.
        DotNetEnv.Env.NoClobber().TraversePath().Load();

        var connection =
            $"Host={Env("POSTGRES_HOST", "localhost")};Port={Env("POSTGRES_PORT", "5433")};"
            + $"Database={Env("POSTGRES_DB", "ragbot")};Username={Env("POSTGRES_USER", "ragbot")};"
            + $"Password={Env("POSTGRES_PASSWORD", "ragbot")}";
        var apiKey = Env("AZURE_OPENAI_API_KEY", "");

        return new Settings(
            Env("AZURE_OPENAI_ENDPOINT", ""),
            apiKey.Length > 0 ? apiKey : null,
            Env("AZURE_OPENAI_EMBEDDING_DEPLOYMENT", "text-embedding-3-small"),
            int.Parse(Env("EMBEDDING_DIMENSIONS", "1536"), System.Globalization.CultureInfo.InvariantCulture),
            Env("AZURE_OPENAI_CHAT_DEPLOYMENT", "gpt-4.1-mini"),
            connection);
    }

    private static string Env(string name, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(name)?.Trim();
        return string.IsNullOrEmpty(value) ? fallback : value;
    }
}
