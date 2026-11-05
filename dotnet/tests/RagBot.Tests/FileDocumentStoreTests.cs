// Mirrors python/tests/test_file_store.py.
// xUnit creates a new instance of the test class per test, and calls Dispose afterwards:
// that's how we get a fresh temp folder per test (≈ pytest's tmp_path).
using RagBot.Core.Domain;
using RagBot.Core.Ingestion.Adapters;
using RagBot.Core.Ingestion.Ports;

namespace RagBot.Tests;

public sealed class FileDocumentStoreTests : IDisposable
{
    private static readonly Uri Url = new("https://www.stavanger.kommune.no/renovasjon-og-miljo/farlig-avfall/");

    // The same document as testdata/stavanger/stored-document.md
    private static readonly Document FixtureDoc = new(
        Url,
        "Farlig avfall: slik leverer du det",
        "# Farlig avfall\n\nLegg batterier i den **røde** beholderen.\n\n- maling\n- lakk",
        new DateTimeOffset(2026, 11, 5, 9, 30, 0, TimeSpan.Zero),
        "renovasjon-og-miljo",
        new DateOnly(2025, 10, 22));

    private readonly string _root = Directory.CreateTempSubdirectory("ragbot-tests-").FullName;

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Path_mirrors_the_url()
    {
        var store = new FileDocumentStore(_root);

        Assert.Equal(
            Path.Combine(_root, "www.stavanger.kommune.no", "renovasjon-og-miljo", "farlig-avfall.md"),
            store.PathFor(Url));
        Assert.Equal(
            Path.Combine(_root, "example.com", "index.md"),
            store.PathFor(new Uri("https://example.com/")));
    }

    [Fact]
    public async Task Saved_file_matches_the_shared_format()
    {
        var store = new FileDocumentStore(_root);

        await store.SaveAsync(FixtureDoc);

        Assert.Equal(TestData.ReadBytes("stored-document.md"), await File.ReadAllBytesAsync(store.PathFor(Url)));
        Assert.Empty(Directory.EnumerateFiles(_root, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Reads_a_file_in_the_shared_format()
    {
        var store = new FileDocumentStore(_root);
        Directory.CreateDirectory(Path.GetDirectoryName(store.PathFor(Url))!);
        File.Copy(TestData.PathOf("stored-document.md"), store.PathFor(Url));

        Assert.Equal(FixtureDoc, await store.GetAsync(Url));
    }

    [Fact]
    public async Task Round_trip_without_optional_fields()
    {
        IDocumentStore store = new FileDocumentStore(_root);
        var doc = new Document(
            new Uri("https://example.com/a/b/"), "", "Hei", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        await store.SaveAsync(doc);

        Assert.Equal(doc, await store.GetAsync(doc.Url));
    }

    [Fact]
    public async Task Missing_url_returns_null()
    {
        Assert.Null(await new FileDocumentStore(_root).GetAsync(Url));
    }

    [Fact]
    public async Task All_returns_every_saved_document()
    {
        var store = new FileDocumentStore(_root);
        var other = new Document(
            new Uri("https://example.com/x/"), "X", "x", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await store.SaveAsync(FixtureDoc);
        await store.SaveAsync(other);
        await store.SaveAsync(FixtureDoc); // overwrite, not duplicate

        var urls = await store.AllAsync().Select(d => d.Url).ToListAsync();

        Assert.Equal(new HashSet<Uri> { Url, other.Url }, urls.ToHashSet());
        Assert.Equal(2, urls.Count);
    }
}
