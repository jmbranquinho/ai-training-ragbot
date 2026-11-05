// Runs vector and keyword search, then fuses the two rankings with Reciprocal Rank Fusion.
// Mirrors python/src/ragbot/chat/retrieval.py.
//
// Each search returns `candidates` hits (at least topK), so a chunk ranked e.g. 12th by one
// method and 3rd by the other can still make the final topK.
using RagBot.Core.Chat.Ports;
using RagBot.Core.Domain;
using RagBot.Core.Indexing.Ports;

namespace RagBot.Core.Chat.Retrieval;

public sealed class HybridRetriever(IEmbedder embedder, IChunkIndex index, int candidates = 20, int rrfK = 60) : IRetriever
{
    private readonly VectorRetriever _vector = new(embedder, index);
    private readonly KeywordRetriever _keyword = new(index);

    public async Task<IReadOnlyList<ScoredChunk>> RetrieveAsync(string question, int topK, CancellationToken ct = default)
    {
        var n = Math.Max(candidates, topK);
        var vector = await _vector.RetrieveAsync(question, n, ct).ConfigureAwait(false);
        var keyword = await _keyword.RetrieveAsync(question, n, ct).ConfigureAwait(false);
        return RankFusion.ReciprocalRankFusion([vector, keyword], rrfK).Take(topK).ToList();
    }
}
