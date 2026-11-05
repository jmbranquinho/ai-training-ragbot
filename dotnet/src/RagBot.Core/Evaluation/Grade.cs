// Mirrors Grade in python/src/ragbot/evaluation/judge.py.
namespace RagBot.Core.Evaluation;

public enum Grade
{
    Correct,
    Partial,
    Incorrect,

    /// <summary>The judge's reply wasn't in the expected format.</summary>
    Invalid,
}
