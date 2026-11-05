// IDocumentStore that keeps each page as a markdown file with a small metadata header.
// Mirrors python/src/ragbot/ingestion/adapters/file_store.py; the format is pinned by
// testdata/stavanger/stored-document.md, so both versions read each other's files.
//
// Layout: <root>/<host>/<url path>.md
//         e.g. data/raw/www.stavanger.kommune.no/renovasjon-og-miljo/farlig-avfall.md
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using RagBot.Core.Domain;
using RagBot.Core.Ingestion.Ports;

namespace RagBot.Core.Ingestion.Adapters;

public sealed partial class FileDocumentStore(string root) : IDocumentStore
{
    private const string Fence = "---";
    private const string DateFormat = "yyyy-MM-dd";
    private const string TimestampFormat = "yyyy-MM-ddTHH:mm:sszzz"; // 2026-11-05T09:30:00+00:00

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public async Task SaveAsync(Document document, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        var path = PathFor(document.Url);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Write to a temp file, then swap it in, so a crash never leaves a half-written file.
        var temp = path + ".tmp";
        await File.WriteAllTextAsync(temp, Serialize(document), Utf8NoBom, ct).ConfigureAwait(false);
        File.Move(temp, path, overwrite: true);
    }

    public async Task<Document?> GetAsync(Uri url, CancellationToken ct = default)
    {
        var path = PathFor(url);
        if (!File.Exists(path))
        {
            return null;
        }

        return Deserialize(await File.ReadAllTextAsync(path, ct).ConfigureAwait(false));
    }

    public async IAsyncEnumerable<Document> AllAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!Directory.Exists(root))
        {
            yield break;
        }

        var paths = Directory.EnumerateFiles(root, "*.md", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            yield return Deserialize(await File.ReadAllTextAsync(path, ct).ConfigureAwait(false));
        }
    }

    public string PathFor(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);
        var segments = url.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => Safe(Uri.UnescapeDataString(s)))
            .ToList();
        if (segments.Count == 0)
        {
            segments.Add("index");
        }

        segments[^1] += ".md";
        return Path.Combine([root, Safe(url.Authority), .. segments]);
    }

    public static string Serialize(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var fields = new List<(string Key, string? Value)>
        {
            ("url", document.Url.ToString()),
            ("title", document.Title),
            ("section", document.Section),
            ("updated_on", document.UpdatedOn?.ToString(DateFormat, CultureInfo.InvariantCulture)),
            ("fetched_at", document.FetchedAt.ToUniversalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture)),
            ("content_hash", document.ContentHash),
        };

        var builder = new StringBuilder().Append(Fence).Append('\n');
        foreach (var (key, value) in fields)
        {
            if (value is null)
            {
                continue;
            }

            if (value.Contains('\n', StringComparison.Ordinal))
            {
                throw new ArgumentException($"{key} cannot contain a line break: {value}", nameof(document));
            }

            builder.Append(key).Append(": ").Append(value).Append('\n');
        }

        return builder.Append(Fence).Append("\n\n").Append(document.Content).Append('\n').ToString();
    }

    public static Document Deserialize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        text = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (!text.StartsWith(Fence + "\n", StringComparison.Ordinal))
        {
            throw new FormatException("missing metadata header");
        }

        var rest = text[(Fence.Length + 1)..];
        var end = rest.IndexOf("\n" + Fence + "\n", StringComparison.Ordinal);
        if (end < 0)
        {
            throw new FormatException("metadata header is not closed");
        }

        var fields = new Dictionary<string, string>();
        foreach (var line in rest[..end].Split('\n'))
        {
            var colon = line.IndexOf(':', StringComparison.Ordinal); // split on the FIRST colon only
            if (colon >= 0)
            {
                fields[line[..colon].Trim()] = line[(colon + 1)..].Trim();
            }
        }

        var body = rest[(end + Fence.Length + 2)..].Trim('\n');
        return new Document(
            new Uri(fields["url"]),
            fields.GetValueOrDefault("title", ""),
            body,
            DateTimeOffset.ParseExact(fields["fetched_at"], TimestampFormat, CultureInfo.InvariantCulture),
            fields.GetValueOrDefault("section"),
            fields.TryGetValue("updated_on", out var updatedOn)
                ? DateOnly.ParseExact(updatedOn, DateFormat, CultureInfo.InvariantCulture)
                : null);
    }

    private static string Safe(string segment)
    {
        return UnsafeChars().Replace(segment, "_");
    }

    [GeneratedRegex("[^A-Za-z0-9._-]")]
    private static partial Regex UnsafeChars();
}
