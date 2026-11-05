// Mirrors RetrievalSummary in python/src/ragbot/evaluation/metrics.py.
namespace RagBot.Core.Evaluation;

/// <param name="HitAt">k → share of questions whose correct page is among the first k results.</param>
/// <param name="Mrr">Mean of 1/rank (0 when not found). 1.0 = always first.</param>
/// <param name="NotFound">Questions whose page wasn't in the results at all.</param>
/// <param name="WorstRank">Largest rank among found ones: "how deep must we go?".</param>
/// <param name="Questions">Number of questions summarized.</param>
public sealed record RetrievalSummary(int Questions, IReadOnlyDictionary<int, double> HitAt, double Mrr, int NotFound, int? WorstRank);
