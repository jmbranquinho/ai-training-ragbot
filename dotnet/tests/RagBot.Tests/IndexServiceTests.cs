// Mirrors python/tests/test_index_service.py.
using Microsoft.Extensions.Logging.Abstractions;
using RagBot.Core.Domain;
using RagBot.Core.Indexing;
using RagBot.Core.Indexing.Adapters;
using RagBot.Core.Indexing.Chunkers;
using RagBot.Core.Ingestion.Adapters;

namespace RagBot.Tests;

public sealed class IndexServiceTests
{
    private static readonly Uri Glass = new("https://x.no/glass/");
    private static readonly Uri Battery = new("https://x.no/batterier/");
    private const string GlassText = "## Glass\n\nGlass og metall leveres i glassbeholderen.";

    private readonly InMemoryDocumentStore _store = new();
    private readonly FakeEmbedder _embedder = new();
    private readonly InMemoryChunkIndex _index = new();

    public IndexServiceTests()
    {
        _store.SaveAsync(Doc(Glass, GlassText)).GetAwaiter().GetResult();
        _store.SaveAsync(Doc(Battery, "## Batterier\n\nBatterier er farlig avfall.\n\n## Maling\n\nOgså.")).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task First_run_embeds_and_indexes_every_page()
    {
        var report = await Service().RunAsync();

        Assert.Equal([Battery, Glass], report.Indexed.OrderBy(u => u.ToString()));
        Assert.Equal(3, report.ChunksEmbedded);
        Assert.StartsWith("headings:1500|", (await _index.DocumentVersionsAsync())[Glass], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Second_run_skips_unchanged_pages_without_calling_the_embedder()
    {
        await Service().RunAsync();
        _embedder.Calls.Clear();

        await _store.SaveAsync(Doc(Glass, "## Glass\n\nNy tekst om glass.")); // one page changed
        var report = await Service().RunAsync();

        Assert.Equal([Glass], report.Indexed);
        Assert.Equal([Battery], report.Unchanged);
        Assert.Single(_embedder.Calls);
    }

    [Fact]
    public async Task Changing_chunker_settings_reindexes_everything()
    {
        await Service().RunAsync();

        var report = await Service(new HeadingChunker(maxChars: 800)).RunAsync();

        Assert.Equal(2, report.Indexed.Count);
    }

    [Fact]
    public async Task Pages_that_became_duplicates_are_removed_from_the_index()
    {
        await Service().RunAsync();

        await _store.SaveAsync(Doc(new Uri("https://x.no/glass/kopi/"), GlassText));
        await _store.SaveAsync(Doc(Battery, GlassText)); // now duplicates Glass (shorter URL wins)
        var report = await Service().RunAsync();

        Assert.Equal([Battery], report.Deleted);
        Assert.Equal([Glass], (await _index.DocumentVersionsAsync()).Keys);
    }

    [Fact]
    public async Task Search_ranks_the_most_similar_chunk_first()
    {
        await Service().RunAsync();

        var results = await _index.SearchAsync(FakeEmbedder.EmbedWords("hvor leverer jeg glass og metall"), topK: 2);

        Assert.Equal(Glass, results[0].Chunk.DocumentUrl);
        Assert.True(results[0].Score > results[1].Score);
    }

    private static Document Doc(Uri url, string content)
    {
        return new Document(url, url.ToString(), content, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }

    private IndexService Service(HeadingChunker? chunker = null)
    {
        return new IndexService(_store, chunker ?? new HeadingChunker(), _embedder, _index, NullLogger<IndexService>.Instance);
    }
}
