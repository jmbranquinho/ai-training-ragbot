// Helpers shared by the chunkers. Mirrors python/src/ragbot/indexing/chunkers/_common.py.
using RagBot.Core.Domain;

namespace RagBot.Core.Indexing.Chunkers;

internal static class ChunkFactory
{
    /// <summary>
    /// Build a Chunk whose text starts with a context header (page title + heading path).
    /// A section such as "Opening hours" doesn't say what it is about; the header adds that
    /// context to the embedding and to the LLM prompt.
    /// </summary>
    public static Chunk Create(Document document, int position, string? heading, string body, string chunker)
    {
        var header = heading is null ? document.Title : $"{document.Title} > {heading}";
        var metadata = new Dictionary<string, string> { ["chunker"] = chunker };
        if (document.Section is not null)
        {
            metadata["section"] = document.Section;
        }

        return new Chunk(
            $"{document.Url}#{position}",
            document.Url,
            document.Title,
            $"{header}\n\n{body}",
            position,
            heading,
            metadata);
    }

    /// <summary>Index of the last whitespace character in text[start..end], or -1.</summary>
    public static int LastWhitespace(string text, int start, int end)
    {
        for (var i = end - 1; i >= start; i--)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                return i;
            }
        }

        return -1;
    }
}
