// IPageParser for MediaWiki sites such as satisfactory.wiki.gg; the logic lives in the pure
// MediaWikiHtml functions.
using RagBot.Core.Domain;

namespace RagBot.Core.Ingestion.Adapters;

public sealed class MediaWikiPageParser : IPageParser
{
    public Document Parse(Uri url, string html, DateTimeOffset fetchedAt)
    {
        return MediaWikiHtml.ParsePage(url, html, fetchedAt);
    }
}
