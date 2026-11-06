// .NET only: the Python version doesn't crawl MediaWiki sites. Never touches the network.
// The fixtures in testdata/mediawiki/ are small hand-written pages with satisfactory.wiki.gg's
// markup (infobox, tabs, navbox, …), not copies of real wiki pages.
using Microsoft.Extensions.Logging.Abstractions;
using RagBot.Core;
using RagBot.Core.Chat;
using RagBot.Core.Evaluation;
using RagBot.Core.Ingestion.Adapters;

namespace RagBot.Tests;

public sealed class MediaWikiTests
{
    private const string Base = "https://example.wiki";
    private static readonly DateTimeOffset FetchedAt = new(2026, 11, 6, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ParsePage_reads_title_category_and_last_edited_date()
    {
        var doc = MediaWikiHtml.ParsePage(new Uri($"{Base}/wiki/Coal_Generator"), TestData.ReadMediaWiki("page.html"), FetchedAt);

        Assert.Equal("Coal Generator", doc.Title);
        Assert.Equal("Buildings", doc.Section); // first category
        Assert.Equal(new DateOnly(2026, 7, 17), doc.UpdatedOn);
        Assert.Equal(FetchedAt, doc.FetchedAt);
    }

    [Fact]
    public void ParsePage_keeps_the_article_and_drops_wiki_chrome()
    {
        var content = MediaWikiHtml.ParsePage(new Uri($"{Base}/wiki/Coal_Generator"), TestData.ReadMediaWiki("page.html"), FetchedAt).Content;

        Assert.Contains("The **Coal Generator** burns Coal and uses Water.", content, StringComparison.Ordinal); // links unwrapped
        Assert.Contains("## Fuel", content, StringComparison.Ordinal);
        Assert.Contains("| Fuel | Items per minute |", content, StringComparison.Ordinal); // <br> in a cell → space
        Assert.Contains("| Coal | 15 |", content, StringComparison.Ordinal);
        foreach (var noise in new[] { "Main page", "hidden helper", "Contents", "edit", "[1]", "Reference text", "Gallery caption", "Categories", "](" })
        {
            Assert.DoesNotContain(noise, content, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ParsePage_flattens_the_infobox_into_label_value_lists()
    {
        var content = MediaWikiHtml.ParsePage(new Uri($"{Base}/wiki/Coal_Generator"), TestData.ReadMediaWiki("page.html"), FetchedAt).Content;

        Assert.Contains("- *Burns coal to make power.*", content, StringComparison.Ordinal);
        Assert.Contains("- Unlocked by: Tier 3", content, StringComparison.Ordinal);
        Assert.Contains("**Building**", content, StringComparison.Ordinal);
        Assert.Contains("- Power generated: 75 MW", content, StringComparison.Ordinal);
        Assert.Contains("- Overclockable: Yes", content, StringComparison.Ordinal); // soft hyphen removed
        Assert.DoesNotContain("### ", content, StringComparison.Ordinal); // no heading per field
    }

    [Fact]
    public void ParsePage_makes_recipe_tables_readable()
    {
        var content = MediaWikiHtml.ParsePage(new Uri($"{Base}/wiki/Coal_Generator"), TestData.ReadMediaWiki("page.html"), FetchedAt).Content;
        var row = content.Split('\n').Single(line => line.StartsWith("| Fancy Plate", StringComparison.Ordinal));

        Assert.Contains("5 × Copper Sheet (25 / min); 20 × Quickwire (100 / min)", row, StringComparison.Ordinal);
        Assert.Contains("Copper Sheet; 50 |", row, StringComparison.Ordinal); // icon-only cell gets its name
        Assert.DoesNotContain("<br>", content, StringComparison.Ordinal);
    }

    [Fact]
    public void ParsePage_labels_tab_panels()
    {
        var content = MediaWikiHtml.ParsePage(new Uri($"{Base}/wiki/Coal_Generator"), TestData.ReadMediaWiki("page.html"), FetchedAt).Content;

        Assert.Contains("**Slow**\n\nSlow setup text.", content, StringComparison.Ordinal);
        Assert.Contains("**Fast**\n\nFast setup text.", content, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseAllPages_returns_articles_and_the_next_page()
    {
        var pageUrl = new Uri($"{Base}/wiki/Special:AllPages?hideredirects=1");

        var (pages, next) = MediaWikiHtml.ParseAllPages(TestData.ReadMediaWiki("allpages.html"), pageUrl);

        Assert.Equal(7, pages.Count); // the redirect is left out
        Assert.DoesNotContain(new Uri($"{Base}/wiki/Old_Name"), pages);
        Assert.Equal(new Uri($"{Base}/wiki/Special:AllPages?from=Miner&hideredirects=1"), next);
        Assert.Null(MediaWikiHtml.ParseAllPages(TestData.ReadMediaWiki("allpages-last.html"), pageUrl).Next); // only "Previous page"
    }

    [Theory]
    [InlineData("/wiki/Assembler/zh", true)]
    [InlineData("/wiki/Caterium_Ore/pt-br", true)]
    [InlineData("/wiki/Coal_Generator/sandbox", true)]
    [InlineData("/wiki/Assembler", false)]
    [InlineData("/wiki/AWESOME_Sink/Theoretical_maximum_of_points", false)]
    public void Translations_and_drafts_are_recognized(string path, bool expected)
    {
        Assert.Equal(expected, MediaWikiAllPagesUrlSource.IsTranslationOrDraft(new Uri(Base + path)));
    }

    [Fact]
    public async Task AllPagesUrlSource_follows_next_pages_and_filters()
    {
        var first = new Uri($"{Base}/wiki/Special:AllPages?hideredirects=1");
        var second = new Uri($"{Base}/wiki/Special:AllPages?from=Miner&hideredirects=1");
        using var client = new HttpClient(new FakeHttpHandler(new()
        {
            [first.ToString()] = FakeHttpHandler.Html(TestData.ReadMediaWiki("allpages.html")),
            [second.ToString()] = FakeHttpHandler.Html(TestData.ReadMediaWiki("allpages-last.html")),
        }));
        var robots = new RobotsPolicy("User-agent: *\nDisallow: /wiki/Water_", "RagBot-test");
        var sleeps = new List<TimeSpan>();
        var source = new MediaWikiAllPagesUrlSource(
            client, robots, new Uri(Base), NullLogger<MediaWikiAllPagesUrlSource>.Instance, TimeSpan.FromSeconds(2),
            (delay, _) => { sleeps.Add(delay); return Task.CompletedTask; });

        var urls = await source.DiscoverAsync();

        Assert.Equal(
            ["/wiki/%3F%3F%3F", "/wiki/Assembler", "/wiki/AWESOME_Sink/Theoretical_maximum_of_points", "/wiki/Coal_Generator", "/wiki/Miner"],
            urls.Select(u => u.AbsolutePath));
        Assert.Equal([TimeSpan.FromSeconds(2)], sleeps); // polite pause between the two list pages
    }

    [Theory]
    [InlineData("/wiki/Coal_Generator", true)]
    [InlineData("/wiki/Special:AllPages?from=Miner&hideredirects=1", true)]
    [InlineData("/api.php?action=query", false)]
    [InlineData("/wiki/File:Coal.png", false)]
    [InlineData("/wiki/Coal?action=edit", false)] // "/*?action="
    [InlineData("/wiki/Coal?x=1&action=history", false)] // "/*?*&action="
    [InlineData("/data/items.json", false)] // "/*.json$"
    [InlineData("/data/items.json?v=2", true)] // "$" anchors at the end
    public void RobotsPolicy_merges_star_groups_and_supports_wildcards(string path, bool allowed)
    {
        var robots = new RobotsPolicy(TestData.ReadMediaWiki("robots.txt"), "RagBot-training/0.1");

        Assert.Equal(allowed, robots.IsAllowed(new Uri(Base + path)));
    }

    [Fact]
    public void RobotsPolicy_uses_only_the_group_naming_our_agent()
    {
        var robots = new RobotsPolicy(TestData.ReadMediaWiki("robots.txt"), "ClaudeBot/1.0");

        Assert.False(robots.IsAllowed(new Uri($"{Base}/wiki/Coal_Generator")));
    }

    [Fact]
    public void Site_profile_fills_the_prompts()
    {
        var site = SiteProfile.Satisfactory;

        var system = PromptBuilder.BuildMessages("How much power?", ChatTestData.Context, site)[0].Content;
        var judge = JudgePrompt.BuildMessages("Q?", "R.", "A.", site)[0].Content;

        Assert.StartsWith("You are a helpful assistant for the Satisfactory Wiki, a community wiki", system, StringComparison.Ordinal);
        Assert.Contains("suggest looking it up on the Satisfactory Wiki directly. Do not guess", system, StringComparison.Ordinal);
        Assert.DoesNotContain("Stavanger", system, StringComparison.Ordinal);
        Assert.StartsWith("You grade answers from a chatbot for the Satisfactory Wiki.", judge, StringComparison.Ordinal);
        Assert.Same(SiteProfile.Satisfactory, SiteProfile.Get("satisfactory"));
        Assert.Throws<ArgumentException>(() => SiteProfile.Get("nope"));
    }
}
