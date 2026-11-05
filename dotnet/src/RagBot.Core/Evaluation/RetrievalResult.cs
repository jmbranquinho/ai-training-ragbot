// Mirrors RetrievalResult in python/src/ragbot/evaluation/service.py.
namespace RagBot.Core.Evaluation;

public sealed record RetrievalResult(string QuestionId, int? Rank);
