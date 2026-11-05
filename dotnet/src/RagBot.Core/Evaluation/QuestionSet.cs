// Loads eval/questions.json. Mirrors load_questions in python/src/ragbot/evaluation/dataset.py.
using System.Text.Json;

namespace RagBot.Core.Evaluation;

public static class QuestionSet
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static IReadOnlyList<EvalQuestion> Load(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.GetProperty("questions").Deserialize<List<EvalQuestion>>(Options)
            ?? throw new InvalidDataException($"no questions in {path}");
    }
}
