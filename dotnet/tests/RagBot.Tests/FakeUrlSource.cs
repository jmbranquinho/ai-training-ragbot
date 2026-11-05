// Test double for IUrlSource. Mirrors FakeUrlSource in python/tests/test_ingest_service.py.
using RagBot.Core.Ingestion.Ports;

namespace RagBot.Tests;

internal sealed class FakeUrlSource(IReadOnlyList<Uri> urls) : IUrlSource
{
    public Task<IReadOnlyList<Uri>> DiscoverAsync(CancellationToken ct = default)
    {
        return Task.FromResult(urls);
    }
}
