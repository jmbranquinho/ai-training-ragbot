// Mirrors DocumentStore in python/src/ragbot/ingestion/ports.py.
using RagBot.Core.Domain;

namespace RagBot.Core.Ingestion.Ports;

public interface IDocumentStore
{
    Task SaveAsync(Document document, CancellationToken ct = default);

    Task<Document?> GetAsync(Uri url, CancellationToken ct = default);

    IAsyncEnumerable<Document> AllAsync(CancellationToken ct = default);
}
