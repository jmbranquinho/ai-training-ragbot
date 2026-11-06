// HTML → Document conversion for MediaWiki sites (written for satisfactory.wiki.gg).
// The counterpart of StavangerHtml. Pure functions (no HTTP, no clock), tested with saved HTML.
//
// Pages are read as normal /wiki/ pages, not through api.php: the wiki's robots.txt disallows
// api.php for crawlers.
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Document = RagBot.Core.Domain.Document; // AngleSharp also has a Document type

namespace RagBot.Core.Ingestion.Adapters;

public static partial class MediaWikiHtml
{
    // Inside the article body, but not content: images and galleries, the table of contents,
    // "[edit]" links, footnote markers, navigation boxes at the bottom, hidden helper elements.
    private const string NoiseSelectors =
        "script, style, noscript, img, figure, audio, video, .thumb, .gallery, #toc, .toc, "
        + ".mw-editsection, sup.reference, .mw-references-wrap, ol.references, .navbox, "
        + ".navbox-styles, .noprint, .mw-empty-elt, [style*='display:none'], [style*='display: none']";

    private static readonly HtmlParser Parser = new();

    private static readonly ReverseMarkdown.Converter Markdown = new(new ReverseMarkdown.Config
    {
        Tags = { Unknown = ReverseMarkdown.Config.UnknownTagsOption.Bypass }, // keep <div> text
        Formatting = { RemoveComments = true, ListBulletChar = '-', OutputLineEnding = "\n" },
    });

    public static Document ParsePage(Uri url, string html, DateTimeOffset fetchedAt)
    {
        ArgumentNullException.ThrowIfNull(url);
        using var doc = Parser.ParseDocument(html);
        var content = doc.QuerySelector("#mw-content-text .mw-parser-output")
            ?? doc.QuerySelector("main") ?? doc.Body ?? doc.DocumentElement;

        var title = doc.QuerySelector("h1#firstHeading")?.TextContent.Trim()
            ?? doc.Title?.Split(" | ")[0].Trim()
            ?? "";
        var category = doc.QuerySelector("#mw-normal-catlinks li")?.TextContent.Trim();
        var updatedOn = ParseLastEdited(doc);

        NameIconLinks(doc, content);

        // Recipe tables: "30 × Quickwire" is followed by a "120 / min" span without a space.
        foreach (var rate in content.QuerySelectorAll(".item-minute").ToList())
        {
            rate.Prepend(doc.CreateTextNode(" ("));
            rate.AppendChild(doc.CreateTextNode(")"));
        }

        foreach (var element in content.QuerySelectorAll(NoiseSelectors).ToList())
        {
            element.Remove();
        }

        LabelTabs(doc, content);
        FlattenInfoboxes(doc, content);

        // Table cells use <br> for line breaks, which markdown tables can't hold.
        foreach (var br in content.QuerySelectorAll("table br").ToList())
        {
            br.Replace(doc.CreateTextNode(" "));
        }

        // Nearly every noun links to another wiki page. As markdown, [Coal](/wiki/Coal) mostly
        // adds noise to chunks and embeddings, so keep only the link text.
        foreach (var link in content.QuerySelectorAll("a").ToList())
        {
            link.Replace([.. link.ChildNodes]);
        }

        return new Document(url, title, ToMarkdown(content.InnerHtml), fetchedAt, category, updatedOn);
    }

    /// <summary>
    /// One page of Special:AllPages: the article links on it, and the "Next page" link
    /// (null on the last page).
    /// </summary>
    public static (IReadOnlyList<Uri> Pages, Uri? Next) ParseAllPages(string html, Uri pageUrl)
    {
        ArgumentNullException.ThrowIfNull(pageUrl);
        using var doc = Parser.ParseDocument(html);

        var pages = new List<Uri>();
        foreach (var anchor in doc.QuerySelectorAll("ul.mw-allpages-chunk li a[href]"))
        {
            if (anchor.ClassList.Contains("mw-redirect"))
            {
                continue;
            }

            if (Uri.TryCreate(pageUrl, anchor.GetAttribute("href"), out var absolute) && absolute.Host == pageUrl.Host)
            {
                pages.Add(new UriBuilder(absolute) { Query = "", Fragment = "" }.Uri);
            }
        }

        var nextHref = doc.QuerySelectorAll(".mw-allpages-nav a[href]")
            .FirstOrDefault(a => a.TextContent.StartsWith("Next page", StringComparison.Ordinal))
            ?.GetAttribute("href");
        var next = nextHref is not null && Uri.TryCreate(pageUrl, nextHref, out var nextUrl) ? nextUrl : null;
        return (pages.Distinct().ToList(), next);
    }

    /// <summary>
    /// Many tables show items as icons only (e.g. a research cost: [icon] 50). The item's name is
    /// only in the link's title, and the icons are removed as noise, so put the name in as text
    /// unless it is already written next to the icon.
    /// </summary>
    private static void NameIconLinks(IDocument doc, IElement content)
    {
        foreach (var link in content.QuerySelectorAll("a[title]").ToList())
        {
            var title = link.GetAttribute("title")!.Trim();
            if (link.TextContent.Trim().Length > 0 || link.QuerySelector("img") is null
                || title.Length == 0 || title.StartsWith("File:", StringComparison.Ordinal)
                || title.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var around = link.ParentElement?.TextContent ?? "";
            if (around.Contains(title, StringComparison.Ordinal))
            {
                link.Remove(); // "[icon] Coal" → "Coal"
            }
            else
            {
                link.Replace(doc.CreateTextNode(title + " ")); // "[icon] 50" → "Copper Sheet 50"
            }
        }
    }

    /// <summary>
    /// Tab panels (e.g. "100% Miner" / "250% Miner") lose their labels once the tab bar is
    /// gone, so put each label in front of its panel.
    /// </summary>
    private static void LabelTabs(IDocument doc, IElement content)
    {
        var labels = content.QuerySelectorAll("a.tabber__tab[aria-controls]")
            .GroupBy(a => a.GetAttribute("aria-controls")!)
            .ToDictionary(g => g.Key, g => g.First().TextContent.Trim());

        foreach (var panel in content.QuerySelectorAll(".tabber__panel").ToList())
        {
            var label = panel.GetAttribute("data-mw-tab-title")
                ?? (panel.Id is not null && labels.TryGetValue(panel.Id, out var text) ? text : null);
            if (!string.IsNullOrWhiteSpace(label))
            {
                var heading = doc.CreateElement("p");
                heading.InnerHtml = $"<strong>{WebUtility.HtmlEncode(label)}</strong>";
                panel.Prepend(heading);
            }
        }

        foreach (var header in content.QuerySelectorAll(".tabber__header").ToList())
        {
            header.Remove();
        }
    }

    /// <summary>
    /// The infobox (power use, size, unlock tier, …) holds many key facts, but its markup is a
    /// heading per field. Converted as-is, every field would become its own markdown heading,
    /// and so its own tiny chunk. Rewrite it as plain "Label: value" lists, one per group.
    /// </summary>
    private static void FlattenInfoboxes(IDocument doc, IElement content)
    {
        foreach (var box in content.QuerySelectorAll("aside.portable-infobox").ToList())
        {
            foreach (var br in box.QuerySelectorAll("br").ToList())
            {
                br.Replace(doc.CreateTextNode(" "));
            }

            var html = new StringBuilder();
            var currentGroup = (string?)null;
            var listOpen = false;
            foreach (var item in box.QuerySelectorAll(".pi-data"))
            {
                var group = Clean(item.Closest("section.pi-group")?.QuerySelector(".pi-header")?.TextContent);
                if (group != currentGroup)
                {
                    if (listOpen)
                    {
                        html.Append("</ul>");
                        listOpen = false;
                    }

                    if (group.Length > 0)
                    {
                        html.Append(CultureInfo.InvariantCulture, $"<p><strong>{WebUtility.HtmlEncode(group)}</strong></p>");
                    }

                    currentGroup = group;
                }

                if (!listOpen)
                {
                    html.Append("<ul>");
                    listOpen = true;
                }

                var label = Clean(item.QuerySelector(".pi-data-label")?.TextContent);
                var value = item.QuerySelector(".pi-data-value")?.InnerHtml ?? "";
                html.Append(label.Length > 0
                    ? $"<li>{WebUtility.HtmlEncode(label)}: {value}</li>"
                    : $"<li>{value}</li>");
            }

            if (listOpen)
            {
                html.Append("</ul>");
            }

            var replacement = doc.CreateElement("div");
            replacement.InnerHtml = html.ToString();
            box.Replace(replacement);
        }
    }

    /// <summary>"This page was last edited on 17 July 2026, at 05:08." → 2026-07-17.</summary>
    private static DateOnly? ParseLastEdited(IDocument doc)
    {
        var text = doc.QuerySelector("#footer-info-lastmod")?.TextContent;
        var match = text is null ? null : LastEditedPattern().Match(text);
        return match is { Success: true }
            && DateOnly.TryParseExact(match.Groups[1].Value, "d MMMM yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
    }

    /// <summary>Collapse whitespace; drop soft hyphens ("Overclock&amp;shy;able").</summary>
    private static string Clean(string? text)
    {
        return text is null ? "" : Whitespace().Replace(text.Replace("­", "", StringComparison.Ordinal), " ").Trim();
    }

    private static string ToMarkdown(string html)
    {
        var markdown = Markdown.Convert(html).Replace(' ', ' ').Replace(' ', ' '); // (narrow) no-break spaces

        // Block elements inside a table cell come out as <br>; a separator reads better.
        markdown = CellBreaks().Replace(markdown, "; ");
        markdown = TrailingSpaces().Replace(markdown, "\n");
        markdown = ExtraBlankLines().Replace(markdown, "\n\n");
        return markdown.Trim();
    }

    [GeneratedRegex(@"last edited on (\d{1,2} \w+ \d{4})")]
    private static partial Regex LastEditedPattern();

    [GeneratedRegex(@"(\s*<br\s*/?>\s*)+")]
    private static partial Regex CellBreaks();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"[ \t]+\r?\n")]
    private static partial Regex TrailingSpaces();

    [GeneratedRegex(@"(\r?\n){3,}")]
    private static partial Regex ExtraBlankLines();
}
