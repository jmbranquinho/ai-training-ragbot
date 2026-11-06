// IUrlSource for MediaWiki sites: walks Special:AllPages (articles only, no redirects).
// The counterpart of SiteMapUrlSource. robots.txt disallows api.php, so the list is read from
// the normal HTML pages, a few hundred titles per page, following "Next page".
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using RagBot.Core.Ingestion.Ports;

namespace RagBot.Core.Ingestion.Adapters;

/// <summary>Discovers the articles of a MediaWiki site, skipping translations and drafts.</summary>
public sealed partial class MediaWikiAllPagesUrlSource(
    HttpClient client,
    RobotsPolicy robots,
    Uri baseUrl,
    ILogger<MediaWikiAllPagesUrlSource> logger,
    TimeSpan? delay = null,
    Func<TimeSpan, CancellationToken, Task>? sleep = null) : IUrlSource
{
    private readonly TimeSpan _delay = delay ?? TimeSpan.FromSeconds(1);
    private readonly Func<TimeSpan, CancellationToken, Task> _sleep = sleep ?? Task.Delay;

    public async Task<IReadOnlyList<Uri>> DiscoverAsync(CancellationToken ct = default)
    {
        var all = new List<Uri>();
        var visited = new HashSet<Uri>();
        Uri? next = new(baseUrl, "/wiki/Special:AllPages?hideredirects=1");

        while (next is not null && visited.Add(next)) // visited: never loop on a bad "next" link
        {
            if (visited.Count > 1)
            {
                await _sleep(_delay, ct).ConfigureAwait(false);
            }

            var html = await client.GetStringAsync(next, ct).ConfigureAwait(false);
            var (pages, nextPage) = MediaWikiHtml.ParseAllPages(html, next);
            all.AddRange(pages);
            next = nextPage;
        }

        var articles = all.Distinct().Where(u => !IsTranslationOrDraft(u)).ToList();
        var allowed = articles.Where(robots.IsAllowed).ToList();
        LogDiscovered(logger, all.Count, all.Count - articles.Count, allowed.Count);
        return allowed;
    }

    /// <summary>
    /// "/wiki/Assembler/zh", "/wiki/Caterium_Ore/pt-br": translated copies of English pages.
    /// "/wiki/X/sandbox": someone's draft. Real subpages ("/wiki/AWESOME_Sink/Theoretical_...")
    /// are kept.
    /// </summary>
    public static bool IsTranslationOrDraft(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);
        return TranslationOrDraft().IsMatch(url.AbsolutePath);
    }

    [GeneratedRegex(@"^/wiki/.+/([a-z]{2,3}(-[a-z]{2,4})?|sandbox)$")]
    private static partial Regex TranslationOrDraft();

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Discovered {Total} pages: {Skipped} translations/drafts skipped, {Allowed} allowed by robots.txt")]
    private static partial void LogDiscovered(ILogger logger, int total, int skipped, int allowed);
}
