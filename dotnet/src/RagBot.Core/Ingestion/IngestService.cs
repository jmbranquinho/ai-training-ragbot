// The ingest use case: discover → fetch → store, writing only what changed.
// Mirrors IngestService in python/src/ragbot/ingestion/service.py.
// Application logic, not an adapter: it only talks to ports, so it works with the real
// website + files, or with fakes in tests.
using Microsoft.Extensions.Logging;
using RagBot.Core.Ingestion.Ports;

namespace RagBot.Core.Ingestion;

public sealed partial class IngestService(
    IUrlSource source,
    IScraper scraper,
    IDocumentStore store,
    ILogger<IngestService> logger)
{
    /// <param name="onlyNew">
    /// Skip pages that are already stored. Lets an interrupted full-site crawl resume where it
    /// stopped, but stored pages are then not checked for changes.
    /// </param>
    /// <param name="maxPages">
    /// Fetch at most this many pages (null = all). Applied after onlyNew, so "--only-new
    /// --max-pages 100" means "the next 100 pages not stored yet".
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<IngestReport> RunAsync(bool onlyNew = false, int? maxPages = null, CancellationToken ct = default)
    {
        var report = new IngestReport();
        IReadOnlyList<Uri> urls = await source.DiscoverAsync(ct).ConfigureAwait(false);
        if (onlyNew)
        {
            var notStored = new List<Uri>();
            foreach (var url in urls)
            {
                if (await store.GetAsync(url, ct).ConfigureAwait(false) is null)
                {
                    notStored.Add(url);
                }
            }

            urls = notStored;
            LogNotStored(logger, urls.Count);
        }

        if (maxPages is not null)
        {
            urls = urls.Take(maxPages.Value).ToList();
        }

        await foreach (var document in scraper.FetchAsync(urls, ct).ConfigureAwait(false))
        {
            var existing = await store.GetAsync(document.Url, ct).ConfigureAwait(false);

            string status;
            if (existing is null)
            {
                report.New.Add(document.Url);
                status = "new";
            }
            else if (existing.ContentHash != document.ContentHash)
            {
                report.Updated.Add(document.Url);
                status = "updated";
            }
            else
            {
                report.Unchanged.Add(document.Url);
                LogStatus(logger, "unchanged", document.Url);
                continue; // keep the existing file (and its original FetchedAt)
            }

            await store.SaveAsync(document, ct).ConfigureAwait(false);
            LogStatus(logger, status, document.Url);
        }

        return report;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{Count} pages not stored yet")]
    private static partial void LogNotStored(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Status,-9}  {Url}")]
    private static partial void LogStatus(ILogger logger, string status, Uri url);
}
