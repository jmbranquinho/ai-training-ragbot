// Test double for IScraper: returns whatever content is currently set for each URL.
// Mirrors FakeScraper in python/tests/test_ingest_service.py.
using System.Runtime.CompilerServices;
using RagBot.Core.Domain;
using RagBot.Core.Ingestion.Ports;

namespace RagBot.Tests;

internal sealed class FakeScraper(DateTimeOffset fetchedAt) : IScraper
{
    public Dictionary<Uri, string> Pages { get; } = [];

    public DateTimeOffset FetchedAt { get; set; } = fetchedAt;

    public async IAsyncEnumerable<Document> FetchAsync(
        IEnumerable<Uri> urls, [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var url in urls)
        {
            yield return new Document(url, url.ToString(), Pages[url], FetchedAt);
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }
}
