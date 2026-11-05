// Mirrors AnswerResult in python/src/ragbot/evaluation/service.py.
namespace RagBot.Core.Evaluation;

/// <param name="CitedExpectedPage">At least one cited source is a correct page.</param>
/// <param name="QuestionId">The question's id.</param>
/// <param name="Answerable">Whether the site answers the question.</param>
/// <param name="Answer">The chatbot's answer text.</param>
/// <param name="SaidUnknown">Whether the answer counted as "I don't know".</param>
/// <param name="Grade">The final grade.</param>
/// <param name="Reason">Why (from the judge, or a fixed rule).</param>
public sealed record AnswerResult(
    string QuestionId,
    bool Answerable,
    string Answer,
    bool SaidUnknown,
    bool CitedExpectedPage,
    Grade Grade,
    string Reason);
