// Mirrors ScoredChunk in python/src/ragbot/domain/models.py.
namespace RagBot.Core.Domain;

/// <summary>A Chunk returned by a search, with its similarity score (higher = more relevant).</summary>
public sealed record ScoredChunk(Chunk Chunk, double Score);
