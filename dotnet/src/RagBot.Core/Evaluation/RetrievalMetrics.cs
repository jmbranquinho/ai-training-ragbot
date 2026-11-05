// Retrieval metrics: pure functions, no I/O. Mirrors python/src/ragbot/evaluation/metrics.py.
//   rank:   position (1-based) of the first retrieved chunk that comes from a correct page.
//   hit@k:  share of questions with rank <= k ("is the right page among the first k?").
//   MRR:    mean of 1/rank (0 when not found).
using RagBot.Core.Domain;

namespace RagBot.Core.Evaluation;

public static class RetrievalMetrics
{
    public static int? FirstHitRank(IReadOnlyList<ScoredChunk> results, IReadOnlyCollection<string> expectedUrls)
    {
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(expectedUrls);
        for (var i = 0; i < results.Count; i++)
        {
            if (expectedUrls.Contains(results[i].Chunk.DocumentUrl.ToString()))
            {
                return i + 1;
            }
        }

        return null;
    }

    public static RetrievalSummary Summarize(IReadOnlyList<int?> ranks, IEnumerable<int> ks)
    {
        ArgumentNullException.ThrowIfNull(ranks);
        if (ranks.Count == 0)
        {
            throw new ArgumentException("no ranks to summarize", nameof(ranks));
        }

        var found = ranks.Where(r => r is not null).Select(r => r!.Value).ToList();
        return new RetrievalSummary(
            ranks.Count,
            ks.ToDictionary(k => k, k => (double)found.Count(r => r <= k) / ranks.Count),
            found.Sum(r => 1.0 / r) / ranks.Count,
            ranks.Count - found.Count,
            found.Count > 0 ? found.Max() : null);
    }
}
