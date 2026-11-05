"""Evaluation use cases: run the question set through a retriever, or through the whole chatbot."""

from collections.abc import Sequence
from dataclasses import dataclass

from ragbot.chat.ports import Retriever
from ragbot.chat.service import ChatService
from ragbot.evaluation.dataset import EvalQuestion
from ragbot.evaluation.judge import AnswerJudge, Grade
from ragbot.evaluation.metrics import RetrievalSummary, first_hit_rank, summarize

KS = (1, 3, 5, 10)


@dataclass(frozen=True)
class RetrievalResult:
    question_id: str
    rank: int | None


def evaluate_retrieval(
    retriever: Retriever, questions: Sequence[EvalQuestion]
) -> tuple[list[RetrievalResult], RetrievalSummary]:
    """Only answerable questions: for the others there is no "right page" to find."""
    results = [
        RetrievalResult(
            q.id, first_hit_rank(retriever.retrieve(q.question, max(KS)), q.expected_urls)
        )
        for q in questions
        if q.answerable
    ]
    return results, summarize([r.rank for r in results], KS)


@dataclass(frozen=True)
class AnswerResult:
    question_id: str
    answerable: bool
    answer: str
    said_unknown: bool
    cited_expected_page: bool  # at least one cited source is a correct page
    grade: Grade
    reason: str


def evaluate_answers(
    chat: ChatService, judge: AnswerJudge, questions: Sequence[EvalQuestion]
) -> list[AnswerResult]:
    results: list[AnswerResult] = []
    for q in questions:
        answer = chat.ask(q.question)
        cited = any(s.document_url in q.expected_urls for s in answer.sources)
        if not q.answerable:  # the right answer is "I don't know": no judge needed
            grade: Grade = "correct" if answer.is_unknown else "incorrect"
            reason = (
                "refused as expected" if answer.is_unknown else "answered an unanswerable question"
            )
        elif answer.is_unknown:
            grade, reason = "incorrect", "said it didn't know"
        else:
            verdict = judge.judge(q.question, q.reference, answer.text)
            grade, reason = verdict.grade, verdict.reason
        results.append(
            AnswerResult(q.id, q.answerable, answer.text, answer.is_unknown, cited, grade, reason)
        )
    return results
