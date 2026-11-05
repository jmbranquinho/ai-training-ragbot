// Mirrors Verdict in python/src/ragbot/evaluation/judge.py.
namespace RagBot.Core.Evaluation;

public sealed record Verdict(Grade Grade, string Reason);
