// In-memory IDocumentStore for development and tests. Swapped for a file store in Step 3.
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using RagBot.Core.Domain;

namespace RagBot.Core.Ingestion.Adapters;

public sealed class InMemoryDocumentStore : IDocumentStore
{
    private readonly ConcurrentDictionary<Uri, Document> _documents = new();

    public Task SaveAsync(Document document, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        _documents[document.Url] = document;
        return Task.CompletedTask;
    }

    public Task<Document?> GetAsync(Uri url, CancellationToken ct = default) =>
        Task.FromResult(_documents.GetValueOrDefault(url));

    public async IAsyncEnumerable<Document> AllAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var document in _documents.Values)
        {
            ct.ThrowIfCancellationRequested();
            yield return document;
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }
}
