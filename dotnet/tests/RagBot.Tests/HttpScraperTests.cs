// Mirrors python/tests/test_http_scraper.py. Never touches the network.
using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using RagBot.Core.Ingestion.Adapters;

namespace RagBot.Tests;

public sealed class HttpScraperTests
{
    private const string Base = "https://www.stavanger.kommune.no";
    private const string Ua = "RagBot-test";

    private static HttpClient MakeClient(Dictionary<string, HttpResponseMessage> routes)
    {
        return new HttpClient(new FakeHttpHandler(routes));
    }

    [Fact]
    public void RobotsPolicy_applies_disallow_prefixes()
    {
        var robots = new RobotsPolicy(TestData.Read("robots.txt"), Ua);

        Assert.True(robots.IsAllowed(new Uri($"{Base}/renovasjon-og-miljo/")));
        Assert.True(robots.IsAllowed(new Uri($"{Base}/renovasjon-og-miljo/finn/")));
        Assert.False(robots.IsAllowed(new Uri($"{Base}/finn/noe/")));
        Assert.False(robots.IsAllowed(new Uri($"{Base}/api/x")));
    }

    [Fact]
    public async Task Missing_robots_txt_allows_everything()
    {
        using var client = MakeClient([]);
        var robots = await RobotsPolicy.LoadAsync(client, new Uri(Base), Ua);

        Assert.True(robots.IsAllowed(new Uri($"{Base}/finn/")));
    }

    [Fact]
    public async Task SiteMapUrlSource_respects_robots_and_max_pages()
    {
        const string siteMap = """
            <a href="/renovasjon-og-miljo/a/">a</a>
            <a href="/renovasjon-og-miljo/b/">b</a>
            <a href="/renovasjon-og-miljo/c/">c</a>
            <a href="/renovasjon-og-miljo/d/">d</a>
            """;
        using var client = MakeClient(new() { [$"{Base}/nettstedskart/"] = FakeHttpHandler.Html(siteMap) });
        var robots = new RobotsPolicy("User-agent: *\nDisallow: /renovasjon-og-miljo/b/", Ua);
        var source = new SiteMapUrlSource(client, robots, new Uri($"{Base}/nettstedskart/"),
            "/renovasjon-og-miljo/", 2, NullLogger<SiteMapUrlSource>.Instance);

        var urls = await source.DiscoverAsync();

        // b is disallowed, so the first 2 allowed pages are a and c
        Assert.Equal(
            [new Uri($"{Base}/renovasjon-og-miljo/a/"), new Uri($"{Base}/renovasjon-og-miljo/c/")],
            urls);
    }

    [Fact]
    public async Task Scraper_returns_documents_and_skips_failures()
    {
        var good = new Uri($"{Base}/renovasjon-og-miljo/farlig-avfall/");
        var missing = new Uri($"{Base}/renovasjon-og-miljo/borte/");
        var pdf = new Uri($"{Base}/renovasjon-og-miljo/fil.pdf");
        var blocked = new Uri($"{Base}/finn/noe/");
        var pdfResponse = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent("%PDF"u8.ToArray()) };
        pdfResponse.Content.Headers.ContentType = new("application/pdf");
        using var client = MakeClient(new()
        {
            [good.ToString()] = FakeHttpHandler.Html(TestData.Read("page.html")),
            [pdf.ToString()] = pdfResponse,
        });
        var sleeps = new List<TimeSpan>();
        var scraper = new HttpScraper(client, new RobotsPolicy(TestData.Read("robots.txt"), Ua),
            NullLogger<HttpScraper>.Instance, TimeSpan.FromSeconds(0.5),
            (delay, _) => { sleeps.Add(delay); return Task.CompletedTask; });

        var docs = await scraper.FetchAsync([good, missing, pdf, blocked]).ToListAsync();

        Assert.Equal([good], docs.Select(d => d.Url));
        Assert.Equal("Batterier, maling og annet farlig avfall", docs[0].Title);
        Assert.Equal([TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(0.5)], sleeps);
    }
}
