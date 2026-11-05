// HTML → Document conversion for stavanger.kommune.no.
// Mirrors python/src/ragbot/ingestion/adapters/stavanger_html.py.
// Pure functions (no HTTP, no clock), so they are easy to test with saved HTML files.
using System.Globalization;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Document = RagBot.Core.Domain.Document; // AngleSharp also has a Document type

namespace RagBot.Core.Ingestion.Adapters;

public static partial class StavangerHtml
{
    // Elements inside <main> that are page chrome, not content.
    private const string NoiseSelectors =
        "script, style, form, aside.byline, ul.anchorlinks, .image-wrapper, .formarea, img";

    // Teaser cards wrap a heading + paragraph in one big <a>. Converted as-is, each card becomes
    // a single markdown link with the heading inside it, which is poor input for embeddings.
    private const string CardLinkSelector = "div.teaser > a";

    private static readonly HtmlParser Parser = new();

    private static readonly ReverseMarkdown.Converter Markdown = new(new ReverseMarkdown.Config
    {
        // Default flavor: the GitHub flavor keeps <section> as raw HTML instead of unwrapping it.
        Tags = { Unknown = ReverseMarkdown.Config.UnknownTagsOption.Bypass }, // keep <div> text
        Links = { SmartHref = true },
        Formatting = { RemoveComments = true, ListBulletChar = '-', OutputLineEnding = "\n" },
    });

    public static Document ParsePage(Uri url, string html, DateTimeOffset fetchedAt)
    {
        ArgumentNullException.ThrowIfNull(url);
        using var doc = Parser.ParseDocument(html);
        var main = doc.QuerySelector("main") ?? doc.Body ?? doc.DocumentElement;

        var updatedOn = ParseUpdatedOn(doc);
        var title = ParseTitle(doc);

        foreach (var element in main.QuerySelectorAll(NoiseSelectors).ToList())
        {
            element.Remove();
        }

        foreach (var cardLink in main.QuerySelectorAll(CardLinkSelector).ToList())
        {
            // Replace the <a> with its children: keep heading + text, drop the link.
            cardLink.Replace([.. cardLink.ChildNodes]);
        }

        return new Document(
            url, title, ToMarkdown(main.InnerHtml), fetchedAt, SectionFromUrl(url), updatedOn);
    }

    /// <summary>All unique same-host links whose path starts with pathPrefix, in page order.</summary>
    public static IReadOnlyList<Uri> ExtractLinks(string html, Uri baseUrl, string pathPrefix)
    {
        ArgumentNullException.ThrowIfNull(baseUrl);
        using var doc = Parser.ParseDocument(html);
        var seen = new HashSet<Uri>();
        var result = new List<Uri>();

        foreach (var anchor in doc.QuerySelectorAll("a[href]"))
        {
            if (!Uri.TryCreate(baseUrl, anchor.GetAttribute("href"), out var absolute)
                || absolute.Scheme is not ("http" or "https")
                || absolute.Host != baseUrl.Host
                || !absolute.AbsolutePath.StartsWith(pathPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var clean = new UriBuilder(absolute) { Query = "", Fragment = "" }.Uri;
            if (seen.Add(clean))
            {
                result.Add(clean);
            }
        }

        return result;
    }

    private static string ParseTitle(IDocument doc)
    {
        return doc.QuerySelector("h1#pagetitle")?.TextContent.Trim()
            ?? doc.Title?.Replace(" | Stavanger kommune", "", StringComparison.Ordinal).Trim()
            ?? "";
    }

    private static DateOnly? ParseUpdatedOn(IDocument doc)
    {
        var text = doc.QuerySelector(".byline-updated")?.TextContent;
        var match = text is null ? null : DatePattern().Match(text);
        return match is { Success: true }
            ? DateOnly.ParseExact(match.Value, "dd.MM.yyyy", CultureInfo.InvariantCulture)
            : null;
    }

    private static string? SectionFromUrl(Uri url)
    {
        return url.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
    }

    private static string ToMarkdown(string html)
    {
        var markdown = Markdown.Convert(html).Replace(' ', ' ');
        markdown = TrailingSpaces().Replace(markdown, "\n");
        markdown = ExtraBlankLines().Replace(markdown, "\n\n");
        return markdown.Trim();
    }

    // [GeneratedRegex] compiles the regex at build time (no Python equivalent needed).
    [GeneratedRegex(@"\d{2}\.\d{2}\.\d{4}")]
    private static partial Regex DatePattern();

    [GeneratedRegex(@"[ \t]+\r?\n")]
    private static partial Regex TrailingSpaces();

    [GeneratedRegex(@"(\r?\n){3,}")]
    private static partial Regex ExtraBlankLines();
}
