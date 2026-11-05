// Mirrors Chunk in python/src/ragbot/domain/models.py.
namespace RagBot.Core.Domain;

/// <summary>A piece of a Document. Keeps a pointer back to its source for citations.</summary>
public sealed record Chunk(
    string Id,
    Uri DocumentUrl,
    string DocumentTitle,
    string Text,
    int Position,
    IReadOnlyDictionary<string, string>? Metadata = null);
