// IPageParser for stavanger.kommune.no; the logic lives in the pure StavangerHtml functions.
using RagBot.Core.Domain;

namespace RagBot.Core.Ingestion.Adapters;

public sealed class StavangerPageParser : IPageParser
{
    public Document Parse(Uri url, string html, DateTimeOffset fetchedAt)
    {
        return StavangerHtml.ParsePage(url, html, fetchedAt);
    }
}
