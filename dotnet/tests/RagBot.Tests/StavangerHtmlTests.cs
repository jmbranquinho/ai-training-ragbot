// Mirrors python/tests/test_stavanger_html.py.
using RagBot.Core.Ingestion.Adapters;

namespace RagBot.Tests;

public sealed class StavangerHtmlTests
{
    private const string Base = "https://www.stavanger.kommune.no";
    private static readonly Uri Url = new($"{Base}/renovasjon-og-miljo/farlig-avfall/");
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 0, 0, 0, TimeSpan.Zero);
    private readonly string _pageHtml = TestData.Read("page.html");

    [Fact]
    public void ParsePage_extracts_metadata()
    {
        var doc = StavangerHtml.ParsePage(Url, _pageHtml, Now);

        Assert.Equal(Url, doc.Url);
        Assert.Equal("Batterier, maling og annet farlig avfall", doc.Title);
        Assert.Equal("renovasjon-og-miljo", doc.Section);
        Assert.Equal(new DateOnly(2025, 10, 22), doc.UpdatedOn);
        Assert.Equal(Now, doc.FetchedAt);
    }

    [Fact]
    public void ParsePage_converts_content_to_markdown()
    {
        var content = StavangerHtml.ParsePage(Url, _pageHtml, Now).Content;

        Assert.StartsWith("# Batterier, maling og annet farlig avfall", content);
        Assert.Contains("## Hva er farlig avfall?", content);
        Assert.Contains("### Trygg levering", content);
        Assert.Contains("- batterier", content);
        Assert.Contains("Legg farlig avfall i den røde beholderen.", content);
        Assert.Contains("[gjenvinningsstasjonen](https://example.org/gjenvinning)", content);
    }

    [Theory] // ≈ a parametrized test: runs once per InlineData
    [InlineData("Del på Facebook")]
    [InlineData("Oppdatert")]
    [InlineData("Var siden nyttig")]
    [InlineData("should be ignored")]
    [InlineData("Main menu")]
    [InlineData("Stavanger kommune")]
    [InlineData("piktogram")]
    [InlineData(" ")]
    public void ParsePage_strips_noise(string noise)
    {
        Assert.DoesNotContain(noise, StavangerHtml.ParsePage(Url, _pageHtml, Now).Content);
    }

    [Fact]
    public void ParsePage_unwraps_teaser_cards()
    {
        var content = StavangerHtml.ParsePage(
            new Uri($"{Base}/renovasjon-og-miljo/klima/"), TestData.Read("landing.html"), Now).Content;

        // Each card becomes a normal heading + paragraph instead of one big link
        Assert.Contains("## Hvordan forbereder vi oss på høyere vannstand?\n\nEn ny rapport", content);
        Assert.Contains("## Best forberedt på et våtere klima\n\nKommunen er kåret", content);
        Assert.DoesNotContain("[##", content);
        Assert.DoesNotContain("klimatilpasning-rapport", content); // the card's link target is dropped

        // Ordinary inline links are untouched
        Assert.Contains("[renovasjon](/renovasjon-og-miljo/)", content);
    }

    [Fact]
    public void ContentHash_changes_only_when_content_changes()
    {
        var first = StavangerHtml.ParsePage(Url, _pageHtml, Now);
        var later = StavangerHtml.ParsePage(Url, _pageHtml, Now.AddMonths(2));
        var edited = StavangerHtml.ParsePage(
            Url, _pageHtml.Replace("batterier", "lysrør", StringComparison.Ordinal), Now);

        Assert.Equal(first.ContentHash, later.ContentHash);
        Assert.NotEqual(first.ContentHash, edited.ContentHash);
    }

    [Fact]
    public void ExtractLinks_keeps_same_host_links_under_prefix()
    {
        var links = StavangerHtml.ExtractLinks(
            TestData.Read("nettstedskart.html"), new Uri($"{Base}/nettstedskart/"), "/renovasjon-og-miljo/");

        Assert.Equal(
            [
                new Uri($"{Base}/renovasjon-og-miljo/"),
                new Uri($"{Base}/renovasjon-og-miljo/farlig-avfall/"), // fragment duplicate removed
                new Uri($"{Base}/renovasjon-og-miljo/matavfall/"), // query string removed
                new Uri($"{Base}/renovasjon-og-miljo/hageavfall/"),
                new Uri($"{Base}/renovasjon-og-miljo/finn/"),
            ],
            links);
    }
}
