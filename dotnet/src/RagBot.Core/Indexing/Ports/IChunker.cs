// Mirrors Chunker in python/src/ragbot/indexing/ports.py.
using RagBot.Core.Domain;

namespace RagBot.Core.Indexing.Ports;

public interface IChunker
{
    /// <summary>Strategy + settings, e.g. "headings:1500". Changing it forces re-indexing.</summary>
    string Signature { get; }

    IReadOnlyList<Chunk> Chunk(Document document);
}
