// Mirrors python/tests/test_in_memory_store.py.
using RagBot.Core.Domain;
using RagBot.Core.Ingestion.Adapters;
using RagBot.Core.Ingestion.Ports;

namespace RagBot.Tests;

public sealed class InMemoryDocumentStoreTests
{
    private static Document MakeDocument(string url = "https://example.com/a")
    {
        return new Document(new Uri(url), "A", "# Hello", DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Saved_document_can_be_retrieved_by_url()
    {
        IDocumentStore store = new InMemoryDocumentStore();
        var doc = MakeDocument();

        await store.SaveAsync(doc);

        Assert.Equal(doc, await store.GetAsync(doc.Url));
    }

    [Fact]
    public async Task Unknown_url_returns_null()
    {
        Assert.Null(await new InMemoryDocumentStore().GetAsync(new Uri("https://nope")));
    }

    [Fact]
    public async Task Saving_same_url_twice_overwrites()
    {
        var store = new InMemoryDocumentStore();
        await store.SaveAsync(MakeDocument());
        await store.SaveAsync(MakeDocument());

        Assert.Single(await store.AllAsync().ToListAsync());
    }
}
