// Mirrors Chunker in python/src/ragbot/indexing/ports.py.
using RagBot.Core.Domain;

namespace RagBot.Core.Indexing.Ports;

public interface IChunker
{
    IReadOnlyList<Chunk> Chunk(Document document);
}
