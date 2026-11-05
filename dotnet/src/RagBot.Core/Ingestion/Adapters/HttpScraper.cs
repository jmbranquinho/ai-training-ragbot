// Mirrors HttpScraper in python/src/ragbot/ingestion/adapters/http_scraper.py.
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using RagBot.Core.Domain;
using RagBot.Core.Ingestion.Ports;

namespace RagBot.Core.Ingestion.Adapters;

/// <summary>Fetches pages politely (robots.txt + delay between requests) and parses them.</summary>
public sealed partial class HttpScraper(
    HttpClient client,
    RobotsPolicy robots,
    ILogger<HttpScraper> logger,
    TimeSpan? delay = null,
    Func<TimeSpan, CancellationToken, Task>? sleep = null) : IScraper // sleep injectable for tests
{
    private readonly TimeSpan _delay = delay ?? TimeSpan.FromSeconds(1);
    private readonly Func<TimeSpan, CancellationToken, Task> _sleep = sleep ?? Task.Delay;

    public async IAsyncEnumerable<Document> FetchAsync(
        IEnumerable<Uri> urls, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var first = true;
        foreach (var url in urls)
        {
            if (!robots.IsAllowed(url))
            {
                LogSkipped(logger, url, "disallowed by robots.txt");
                continue;
            }

            if (!first)
            {
                await _sleep(_delay, ct).ConfigureAwait(false);
            }

            first = false;

            var document = await TryFetchAsync(url, ct).ConfigureAwait(false);
            if (document is not null)
            {
                yield return document;
            }
        }
    }

    // C# can't `yield` inside try/catch, so the fetch lives in its own method.
    private async Task<Document?> TryFetchAsync(Uri url, CancellationToken ct)
    {
        try
        {
            using var response = await client.GetAsync(url, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                LogSkipped(logger, url, $"HTTP {(int)response.StatusCode}");
                return null;
            }

            if (response.Content.Headers.ContentType?.MediaType != "text/html")
            {
                LogSkipped(logger, url, "not HTML");
                return null;
            }

            var html = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return StavangerHtml.ParsePage(url, html, DateTimeOffset.UtcNow);
        }
        catch (HttpRequestException error)
        {
            LogSkipped(logger, url, error.Message);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipping {Url} ({Reason})")]
    private static partial void LogSkipped(ILogger logger, Uri url, string reason);
}
