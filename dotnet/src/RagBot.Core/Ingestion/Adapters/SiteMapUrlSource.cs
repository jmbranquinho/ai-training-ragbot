// Mirrors SiteMapUrlSource in python/src/ragbot/ingestion/adapters/http_scraper.py.
using Microsoft.Extensions.Logging;
using RagBot.Core.Ingestion.Ports;

namespace RagBot.Core.Ingestion.Adapters;

/// <summary>Discovers pages from the site's HTML site map (/nettstedskart/).</summary>
public sealed partial class SiteMapUrlSource(
    HttpClient client,
    RobotsPolicy robots,
    Uri siteMapUrl,
    string pathPrefix,
    ILogger<SiteMapUrlSource> logger) : IUrlSource // primary constructor (C# 12)
{
    public async Task<IReadOnlyList<Uri>> DiscoverAsync(CancellationToken ct = default)
    {
        var html = await client.GetStringAsync(siteMapUrl, ct).ConfigureAwait(false);
        var links = StavangerHtml.ExtractLinks(html, siteMapUrl, pathPrefix);

        // The site map links to itself; it's only a list of links, not content worth indexing
        var allowed = links.Where(u => u != siteMapUrl && robots.IsAllowed(u)).ToList();
        LogDiscovered(logger, links.Count, allowed.Count);
        return allowed;
    }

    // [LoggerMessage] generates fast, strongly typed log methods (recommended by CA1848).
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Discovered {Total} pages ({Allowed} allowed by robots.txt)")]
    private static partial void LogDiscovered(ILogger logger, int total, int allowed);
}
