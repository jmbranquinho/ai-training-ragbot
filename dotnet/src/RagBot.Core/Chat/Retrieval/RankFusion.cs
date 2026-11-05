// Mirrors reciprocal_rank_fusion in python/src/ragbot/chat/retrieval.py.
using RagBot.Core.Domain;

namespace RagBot.Core.Chat.Retrieval;

public static class RankFusion
{
    /// <summary>
    /// Merge rankings by position only: score(chunk) = sum over lists of 1 / (k + rank).
    /// Raw scores are ignored on purpose: cosine similarity and full-text rank are on different
    /// scales. k = 60 is the value from the original RRF paper (Cormack et al., 2009); it keeps
    /// one list's top hit from dominating. Ties are broken by chunk id, so results are stable.
    /// </summary>
    public static IReadOnlyList<ScoredChunk> ReciprocalRankFusion(IEnumerable<IReadOnlyList<ScoredChunk>> rankings, int k = 60)
    {
        ArgumentNullException.ThrowIfNull(rankings);
        var scores = new Dictionary<string, double>();
        var chunks = new Dictionary<string, Chunk>();

        foreach (var ranking in rankings)
        {
            for (var i = 0; i < ranking.Count; i++)
            {
                var id = ranking[i].Chunk.Id;
                scores[id] = scores.GetValueOrDefault(id) + (1.0 / (k + i + 1)); // rank is 1-based
                chunks[id] = ranking[i].Chunk;
            }
        }

        return scores
            .OrderByDescending(s => s.Value)
            .ThenBy(s => s.Key, StringComparer.Ordinal)
            .Select(s => new ScoredChunk(chunks[s.Key], s.Value))
            .ToList();
    }
}
