// Turns a downloaded HTML page into a Document. Each website has its own layout, so HttpScraper
// takes the parser for the site it crawls (StavangerPageParser, MediaWikiPageParser).
// Not a use-case port: IngestService never sees it. It's a seam inside the scraper adapter.
using RagBot.Core.Domain;

namespace RagBot.Core.Ingestion.Adapters;

public interface IPageParser
{
    Document Parse(Uri url, string html, DateTimeOffset fetchedAt);
}
