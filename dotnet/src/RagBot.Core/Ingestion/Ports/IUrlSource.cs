// Mirrors UrlSource in python/src/ragbot/ingestion/ports.py.
namespace RagBot.Core.Ingestion.Ports;

public interface IUrlSource
{
    /// <summary>Return the URLs that should be scraped.</summary>
    Task<IReadOnlyList<Uri>> DiscoverAsync(CancellationToken ct = default);
}
