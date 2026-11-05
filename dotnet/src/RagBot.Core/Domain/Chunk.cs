// Mirrors Chunk in python/src/ragbot/domain/models.py.
namespace RagBot.Core.Domain;

/// <summary>A piece of a Document. Keeps a pointer back to its source for citations.</summary>
/// <param name="Id">"&lt;document url&gt;#&lt;position&gt;": stable, so re-indexing can overwrite.</param>
/// <param name="DocumentUrl">URL of the page the chunk came from.</param>
/// <param name="DocumentTitle">Title of that page.</param>
/// <param name="Text">What gets embedded: a context header line + the chunk body.</param>
/// <param name="Position">Order of this chunk within its document.</param>
/// <param name="Heading">Heading path inside the page, e.g. "Levering &gt; Gjenbruk".</param>
/// <param name="Metadata">Extra key/values, e.g. "section" and the "chunker" that made it.</param>
public sealed record Chunk(
    string Id,
    Uri DocumentUrl,
    string DocumentTitle,
    string Text,
    int Position,
    string? Heading = null,
    IReadOnlyDictionary<string, string>? Metadata = null);
