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

    [Fact]
    public async Task Only_new_skips_pages_that_are_already_stored()
    {
        var store = new InMemoryDocumentStore();
        var scraper = new FakeScraper(FirstRun) { Pages = { [A] = "A" } };
        await new IngestService(new FakeUrlSource([A]), scraper, store, NullLogger<IngestService>.Instance).RunAsync();

        scraper.Pages[A] = "A, edited"; // A changed, B is new
        scraper.Pages[B] = "B";
        var report = await CreateService(scraper, store).RunAsync(onlyNew: true);

        Assert.Equal([B], report.New);
        Assert.Empty(report.Updated); // A was not fetched at all, so its change is not seen
        Assert.Equal("A", (await store.GetAsync(A))!.Content);
    }

    [Fact]
    public async Task Max_pages_counts_only_pages_that_are_not_stored_yet()
    {
        var store = new InMemoryDocumentStore();
        var scraper = new FakeScraper(FirstRun) { Pages = { [A] = "A", [B] = "B" } };
        await CreateService(scraper, store).RunAsync(maxPages: 1); // stores A

        var report = await CreateService(scraper, store).RunAsync(onlyNew: true, maxPages: 1);

        Assert.Equal([B], report.New); // the next page not stored yet, not A again
    }

    private static IngestService CreateService(FakeScraper scraper, InMemoryDocumentStore store)
    {
        return new IngestService(
            new FakeUrlSource([A, B]), scraper, store, NullLogger<IngestService>.Instance);
    }
}
