// Mirrors Scraper in python/src/ragbot/ingestion/ports.py.
using RagBot.Core.Domain;

namespace RagBot.Core.Ingestion.Ports;

public interface IScraper
{
    IAsyncEnumerable<Document> FetchAsync(IEnumerable<Uri> urls, CancellationToken ct = default);
}
