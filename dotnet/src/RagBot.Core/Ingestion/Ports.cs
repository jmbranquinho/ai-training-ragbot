// Interfaces for the ingestion feature. Mirrors python/src/ragbot/ingestion/ports.py.
using RagBot.Core.Domain;

namespace RagBot.Core.Ingestion;

public interface IScraper
{
    IAsyncEnumerable<Document> FetchAsync(IEnumerable<Uri> urls, CancellationToken ct = default);
}

public interface IDocumentStore
{
    Task SaveAsync(Document document, CancellationToken ct = default);

    Task<Document?> GetAsync(Uri url, CancellationToken ct = default);

    IAsyncEnumerable<Document> AllAsync(CancellationToken ct = default);
}
