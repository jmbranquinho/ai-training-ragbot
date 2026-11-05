// Drop pages whose content is identical to another page's.
// Mirrors python/src/ragbot/indexing/deduplicate.py.
//
// stavanger.kommune.no serves some long plans in full on every chapter URL (one plan appears
// under 81 URLs). Indexing all copies would fill every search result with the same text, so
// we keep one page per content hash. The raw store keeps all copies: it records what the site
// serves, and this decision belongs to indexing.
using RagBot.Core.Domain;

namespace RagBot.Core.Indexing;

public static class DocumentDeduplicator
{
    /// <summary>
    /// One document per content hash: the one with the shortest URL (ties: alphabetical).
    /// For the plans, the shortest URL is the plan's root page, which is a good page to cite.
    /// Order of the result follows the first occurrence of each content.
    /// </summary>
    public static IReadOnlyList<Document> Deduplicate(IEnumerable<Document> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        var order = new List<string>();
        var best = new Dictionary<string, Document>();

        foreach (var doc in documents)
        {
            var hash = doc.ContentHash;
            if (!best.TryGetValue(hash, out var current))
            {
                order.Add(hash);
                best[hash] = doc;
            }
            else if (IsBetter(doc.Url.ToString(), current.Url.ToString()))
            {
                best[hash] = doc;
            }
        }

        return order.Select(hash => best[hash]).ToList();
    }

    private static bool IsBetter(string candidate, string current)
    {
        return candidate.Length != current.Length
            ? candidate.Length < current.Length
            : string.CompareOrdinal(candidate, current) < 0;
    }
}
