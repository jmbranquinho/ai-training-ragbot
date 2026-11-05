// Domain models: the "what". Mirrors python/src/ragbot/domain/models.py.
namespace RagBot.Core.Domain;

/// <summary>A single page fetched from the source website, converted to markdown.</summary>
public sealed record Document(
    Uri Url,
    string Title,
    string Content,
    DateTimeOffset FetchedAt,
    string? Section = null);

/// <summary>A piece of a Document. Keeps a pointer back to its source for citations.</summary>
public sealed record Chunk(
    string Id,
    Uri DocumentUrl,
    string DocumentTitle,
    string Text,
    int Position,
    IReadOnlyDictionary<string, string>? Metadata = null);

/// <summary>A Chunk returned by a search, with its similarity score (higher = more relevant).</summary>
public sealed record ScoredChunk(Chunk Chunk, double Score);

/// <summary>The chatbot's reply. Sources should point back to where the answer came from.</summary>
public sealed record Answer(string Text, IReadOnlyList<Chunk> Sources)
{
    /// <summary>True when nothing relevant was found and the bot declined to answer.</summary>
    public bool IsUnknown => Sources.Count == 0;
}
