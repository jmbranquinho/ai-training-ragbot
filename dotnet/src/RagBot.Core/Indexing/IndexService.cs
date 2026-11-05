// The indexing use case: stored pages → de-duplicate → chunk → embed → index.
// Mirrors IndexService in python/src/ragbot/indexing/service.py.
//
// Works page by page and skips pages whose version is already indexed. A version is
// "<chunker signature>|<content hash>", so a changed page *or* changed chunker settings
// re-embeds that page, while unchanged pages cost nothing.
using Microsoft.Extensions.Logging;
using RagBot.Core.Indexing.Ports;
using RagBot.Core.Ingestion.Ports;

namespace RagBot.Core.Indexing;

public sealed partial class IndexService(
    IDocumentStore store,
    IChunker chunker,
    IEmbedder embedder,
    IChunkIndex index,
    ILogger<IndexService> logger)
{
    public async Task<IndexReport> RunAsync(bool rebuild = false, CancellationToken ct = default)
    {
        var report = new IndexReport();
        if (rebuild)
        {
            await index.ClearAsync(ct).ConfigureAwait(false);
        }

        var stored = await store.AllAsync(ct).ToListAsync(ct).ConfigureAwait(false);
        var documents = DocumentDeduplicator.Deduplicate(stored);
        var indexedVersions = await index.DocumentVersionsAsync(ct).ConfigureAwait(false);

        foreach (var document in documents)
        {
            var version = $"{chunker.Signature}|{document.ContentHash}";
            if (indexedVersions.TryGetValue(document.Url, out var existing) && existing == version)
            {
                report.Unchanged.Add(document.Url);
                continue;
            }

            var chunks = chunker.Chunk(document);
            var vectors = await embedder.EmbedAsync(chunks.Select(c => c.Text).ToList(), ct).ConfigureAwait(false);
            await index.ReplaceDocumentAsync(document.Url, version, chunks, vectors, ct).ConfigureAwait(false);
            report.Indexed.Add(document.Url);
            report.ChunksEmbedded += chunks.Count;
            LogIndexed(logger, chunks.Count, document.Url);
        }

        // Pages that disappeared from the store (or became duplicates) leave the index too
        var current = documents.Select(d => d.Url).ToHashSet();
        foreach (var url in indexedVersions.Keys.Where(u => !current.Contains(u)))
        {
            await index.DeleteDocumentAsync(url, ct).ConfigureAwait(false);
            report.Deleted.Add(url);
            LogDeleted(logger, url);
        }

        return report;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "indexed  {Count,3} chunks  {Url}")]
    private static partial void LogIndexed(ILogger logger, int count, Uri url);

    [LoggerMessage(Level = LogLevel.Information, Message = "deleted  {Url}")]
    private static partial void LogDeleted(ILogger logger, Uri url);
}
