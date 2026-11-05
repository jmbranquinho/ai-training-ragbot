// Semantic similarity (embeddings). Handles synonyms, paraphrases and other languages, but can
// miss exact terms such as product names or codes. Mirrors python/src/ragbot/chat/retrieval.py.
using RagBot.Core.Chat.Ports;
using RagBot.Core.Domain;
using RagBot.Core.Indexing.Ports;

namespace RagBot.Core.Chat.Retrieval;

public sealed class VectorRetriever(IEmbedder embedder, IChunkIndex index) : IRetriever
{
    public async Task<IReadOnlyList<ScoredChunk>> RetrieveAsync(string question, int topK, CancellationToken ct = default)
    {
        var vectors = await embedder.EmbedAsync([question], ct).ConfigureAwait(false);
        return await index.SearchAsync(vectors[0], topK, ct).ConfigureAwait(false);
    }
}
