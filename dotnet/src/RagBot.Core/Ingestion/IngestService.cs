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
    public async Task<IngestReport> RunAsync(CancellationToken ct = default)
    {
        var report = new IngestReport();
        var urls = await source.DiscoverAsync(ct).ConfigureAwait(false);

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

    [LoggerMessage(Level = LogLevel.Information, Message = "{Status,-9}  {Url}")]
    private static partial void LogStatus(ILogger logger, string status, Uri url);
}
