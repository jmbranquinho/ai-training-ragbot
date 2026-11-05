// Mirrors python/tests/test_deduplicate.py.
using RagBot.Core.Domain;
using RagBot.Core.Indexing;

namespace RagBot.Tests;

public sealed class DocumentDeduplicatorTests
{
    private static Document Doc(string url, string content)
    {
        return new Document(new Uri(url), url, content, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Keeps_one_page_per_content_with_the_shortest_url()
    {
        var result = DocumentDeduplicator.Deduplicate(
        [
            Doc("https://x.no/plan/kapittel-1/", "hele planen"),
            Doc("https://x.no/annet/", "noe annet"),
            Doc("https://x.no/plan/", "hele planen"),
            Doc("https://x.no/plan/kapittel-2/", "hele planen"),
        ]);

        Assert.Equal(["https://x.no/plan/", "https://x.no/annet/"], result.Select(d => d.Url.ToString()));
    }

    [Fact]
    public void Equal_length_urls_pick_the_alphabetically_first()
    {
        var result = DocumentDeduplicator.Deduplicate([Doc("https://x.no/b/", "same"), Doc("https://x.no/a/", "same")]);

        Assert.Equal(["https://x.no/a/"], result.Select(d => d.Url.ToString()));
    }
}
