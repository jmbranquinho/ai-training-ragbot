// Mirrors python/tests/test_ingest_service.py.
using Microsoft.Extensions.Logging.Abstractions;
using RagBot.Core.Ingestion;
using RagBot.Core.Ingestion.Adapters;

namespace RagBot.Tests;

public sealed class IngestServiceTests
{
    private static readonly DateTimeOffset FirstRun = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly Uri A = new("https://example.com/a/");
    private static readonly Uri B = new("https://example.com/b/");

    [Fact]
    public async Task First_run_stores_everything_as_new()
    {
        var store = new InMemoryDocumentStore();
        var scraper = new FakeScraper(FirstRun) { Pages = { [A] = "A", [B] = "B" } };

        var report = await CreateService(scraper, store).RunAsync();

        Assert.Equal([A, B], report.New);
        Assert.Empty(report.Updated);
        Assert.Empty(report.Unchanged);
        Assert.Equal(2, (await store.AllAsync().ToListAsync()).Count);
    }

    [Fact]
    public async Task Second_run_detects_updated_and_unchanged_pages()
    {
        var store = new InMemoryDocumentStore();
        var scraper = new FakeScraper(FirstRun) { Pages = { [A] = "A", [B] = "B" } };
        await CreateService(scraper, store).RunAsync();

        scraper.Pages[B] = "B, edited"; // the website changed one page
        scraper.FetchedAt = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
        var report = await CreateService(scraper, store).RunAsync();

        Assert.Empty(report.New);
        Assert.Equal([B], report.Updated);
        Assert.Equal([A], report.Unchanged);
        Assert.Equal(FirstRun, (await store.GetAsync(A))!.FetchedAt); // unchanged page not rewritten
        Assert.Equal("B, edited", (await store.GetAsync(B))!.Content);
    }

    private static IngestService CreateService(FakeScraper scraper, InMemoryDocumentStore store)
    {
        return new IngestService(
            new FakeUrlSource([A, B]), scraper, store, NullLogger<IngestService>.Instance);
    }
}
