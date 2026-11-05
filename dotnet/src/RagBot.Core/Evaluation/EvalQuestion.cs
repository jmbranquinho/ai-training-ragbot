// One question from eval/questions.json. Mirrors EvalQuestion in python/src/ragbot/evaluation/dataset.py.
using System.Text.Json.Serialization;

namespace RagBot.Core.Evaluation;

/// <param name="Id">Stable id, e.g. "q01".</param>
/// <param name="Language">"no" or "en".</param>
/// <param name="Question">The question as a resident would ask it.</param>
/// <param name="ExpectedUrls">Pages that contain the answer; any one counts as a hit.</param>
/// <param name="Reference">The key facts, for the answer judge.</param>
public sealed record EvalQuestion(
    string Id,
    string Language,
    string Question,
    [property: JsonPropertyName("expected_urls")] IReadOnlyList<string> ExpectedUrls,
    string Reference)
{
    /// <summary>False when the site doesn't answer it: then "I don't know" is the right answer.</summary>
    [JsonIgnore]
    public bool Answerable
    {
        get
        {
            return ExpectedUrls.Count > 0;
        }
    }
}
