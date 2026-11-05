// Chunk with fixed-size, overlapping windows over the whole page.
// Mirrors python/src/ragbot/indexing/chunkers/sliding_window.py.
//
// Ignores the page structure. Windows end at the last whitespace before size, and the next
// window starts overlap characters earlier (at a word start), so a sentence cut at a
// boundary still appears whole in one of the two neighbouring chunks.
using RagBot.Core.Domain;
using RagBot.Core.Indexing.Ports;

namespace RagBot.Core.Indexing.Chunkers;

public sealed class SlidingWindowChunker : IChunker
{
    public const string Name = "window";

    private readonly int _size;
    private readonly int _overlap;

    public SlidingWindowChunker(int size = 1000, int overlap = 200)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 100);
        ArgumentOutOfRangeException.ThrowIfNegative(overlap);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(overlap, size / 2);
        _size = size;
        _overlap = overlap;
    }

    public IReadOnlyList<Chunk> Chunk(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var text = document.Content;
        var chunks = new List<Chunk>();
        var start = 0;

        while (start < text.Length)
        {
            var end = Math.Min(start + _size, text.Length);
            if (end < text.Length) // not the last window: end at a word boundary
            {
                var cut = ChunkFactory.LastWhitespace(text, start + (_size / 2), end);
                if (cut > 0)
                {
                    end = cut;
                }
            }

            var piece = text[start..end].Trim();
            if (piece.Length > 0)
            {
                chunks.Add(ChunkFactory.Create(document, chunks.Count, null, piece, Name));
            }

            if (end >= text.Length)
            {
                break;
            }

            start = end - _overlap;
            while (start < end && !char.IsWhiteSpace(text[start - 1])) // move to a word start
            {
                start++;
            }
        }

        return chunks;
    }
}
