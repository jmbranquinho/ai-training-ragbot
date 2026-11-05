// IEmbedder backed by an Azure OpenAI / Azure AI Foundry embedding deployment.
// Mirrors python/src/ragbot/indexing/adapters/azure_embedder.py.
using Azure.AI.OpenAI;
using OpenAI.Embeddings;
using RagBot.Core.Indexing.Ports;

namespace RagBot.Core.Indexing.Adapters;

public sealed class AzureOpenAIEmbedder : IEmbedder
{
    private readonly EmbeddingClient _client;
    private readonly EmbeddingGenerationOptions _options;
    private readonly int _batchSize;

    public AzureOpenAIEmbedder(AzureOpenAIClient client, string deployment, int dimensions, int batchSize = 64)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client.GetEmbeddingClient(deployment);
        _options = new EmbeddingGenerationOptions { Dimensions = dimensions };
        _batchSize = batchSize;
    }

    public async Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedAsync(
        IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(texts);
        var vectors = new List<ReadOnlyMemory<float>>(texts.Count);
        foreach (var batch in texts.Chunk(_batchSize)) // several texts per request
        {
            var response = await _client.GenerateEmbeddingsAsync(batch, _options, ct).ConfigureAwait(false);
            vectors.AddRange(response.Value.OrderBy(e => e.Index).Select(e => e.ToFloats()));
        }

        return vectors;
    }
}
