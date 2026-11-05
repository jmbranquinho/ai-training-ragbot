// Chunk by markdown headings: one chunk per section, so each chunk is about one topic.
// Mirrors python/src/ragbot/indexing/chunkers/headings.py.
//
// Sections longer than maxChars are split further at paragraph breaks (blank lines), and a
// single paragraph that is still too long is split at the last space before the limit.
// maxChars limits the body; the context header is added on top.
using System.Text.RegularExpressions;
using RagBot.Core.Domain;
using RagBot.Core.Indexing.Ports;

namespace RagBot.Core.Indexing.Chunkers;

public sealed partial class HeadingChunker : IChunker
{
    public const string Name = "headings";

    private readonly int _maxChars;

    public HeadingChunker(int maxChars = 1500)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxChars, 100);
        _maxChars = maxChars;
    }

    public IReadOnlyList<Chunk> Chunk(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var chunks = new List<Chunk>();
        foreach (var (heading, body) in Sections(document.Content, document.Title))
        {
            foreach (var piece in SplitLong(body, _maxChars))
            {
                chunks.Add(ChunkFactory.Create(document, chunks.Count, heading, piece, Name));
            }
        }

        return chunks;
    }

    /// <summary>Split into (heading path, body) pairs. Sections without a body are dropped.</summary>
    private static List<(string? Heading, string Body)> Sections(string markdown, string title)
    {
        var sections = new List<(string?, string)>();
        var path = new List<(int Level, string Text)>(); // used as a stack
        var lines = new List<string>();

        void Flush()
        {
            var body = string.Join('\n', lines).Trim();
            if (body.Length > 0)
            {
                var names = path.Select(p => p.Text).ToList();
                if (names.Count > 0 && names[0] == title) // the h1 repeats the page title
                {
                    names.RemoveAt(0);
                }

                sections.Add((names.Count > 0 ? string.Join(" > ", names) : null, body));
            }

            lines.Clear();
        }

        foreach (var line in markdown.Split('\n'))
        {
            var match = HeadingPattern().Match(line.TrimEnd('\r'));
            if (!match.Success)
            {
                lines.Add(line.TrimEnd('\r'));
                continue;
            }

            Flush();
            var level = match.Groups[1].Length;
            while (path.Count > 0 && path[^1].Level >= level) // leaving deeper (or same-level) sections
            {
                path.RemoveAt(path.Count - 1);
            }

            path.Add((level, match.Groups[2].Value));
        }

        Flush();
        return sections;
    }

    private static List<string> SplitLong(string text, int maxChars)
    {
        if (text.Length <= maxChars)
        {
            return [text];
        }

        var pieces = new List<string>();
        var current = "";
        foreach (var raw in text.Split("\n\n"))
        {
            var paragraph = raw.Trim();
            if (paragraph.Length == 0)
            {
                continue;
            }

            while (paragraph.Length > maxChars) // one paragraph alone is too long: hard split
            {
                var cut = ChunkFactory.LastWhitespace(paragraph, 1, maxChars + 1);
                cut = cut > 0 ? cut : maxChars;
                if (current.Length > 0)
                {
                    pieces.Add(current);
                    current = "";
                }

                pieces.Add(paragraph[..cut].TrimEnd());
                paragraph = paragraph[cut..].TrimStart();
            }

            var candidate = current.Length > 0 ? $"{current}\n\n{paragraph}" : paragraph;
            if (candidate.Length <= maxChars)
            {
                current = candidate;
            }
            else
            {
                pieces.Add(current);
                current = paragraph;
            }
        }

        if (current.Length > 0)
        {
            pieces.Add(current);
        }

        return pieces;
    }

    [GeneratedRegex(@"^(#{1,6})\s+(.+?)\s*#*\s*$")] // "## Title" → level 2, "Title"
    private static partial Regex HeadingPattern();
}
