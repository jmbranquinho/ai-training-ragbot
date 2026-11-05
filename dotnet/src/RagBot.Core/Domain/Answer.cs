// Mirrors Answer in python/src/ragbot/domain/models.py.
namespace RagBot.Core.Domain;

/// <summary>The chatbot's reply. Sources should point back to where the answer came from.</summary>
public sealed record Answer(string Text, IReadOnlyList<Chunk> Sources)
{
    /// <summary>True when nothing relevant was found and the bot declined to answer.</summary>
    public bool IsUnknown
    {
        get
        {
            return Sources.Count == 0;
        }
    }
}
