"""Evaluation tests. The .NET suite (EvaluationTests.cs) uses the same inputs and expectations."""

from collections.abc import Sequence
from pathlib import Path

import pytest

from ragbot.chat.service import ChatService
from ragbot.domain import Answer, Chunk, ScoredChunk
from ragbot.evaluation.dataset import EvalQuestion, load_questions
from ragbot.evaluation.judge import Verdict, build_judge_messages, parse_verdict
from ragbot.evaluation.metrics import first_hit_rank, summarize
from ragbot.evaluation.service import evaluate_answers


def hit(url: str) -> ScoredChunk:
    return ScoredChunk(Chunk(id=url, document_url=url, document_title="T", text="t", position=0), 1)


def test_first_hit_rank_is_the_position_of_the_first_correct_page() -> None:
    results = [hit("https://x.no/a/"), hit("https://x.no/b/"), hit("https://x.no/c/")]

    assert first_hit_rank(results, ["https://x.no/c/", "https://x.no/b/"]) == 2
    assert first_hit_rank(results, ["https://x.no/z/"]) is None


def test_summarize_computes_hit_at_k_and_mrr() -> None:
    summary = summarize([1, 2, None, 4], ks=[1, 3, 5])

    assert summary.hit_at == {1: 0.25, 3: 0.5, 5: 0.75}
    assert summary.mrr == pytest.approx((1 + 1 / 2 + 0 + 1 / 4) / 4)
    assert summary.not_found == 1
    assert summary.worst_rank == 4


def test_judge_prompt_matches_the_shared_fixture(testdata_dir: Path) -> None:
    expected = (testdata_dir.parent / "eval" / "expected-judge-prompt.txt").read_text("utf-8")
    messages = build_judge_messages("Spørsmål?", "Fasit.", "Svar [1].")

    assert "".join(f"=== {m['role']} ===\n{m['content']}\n" for m in messages) == expected


@pytest.mark.parametrize(
    ("reply", "grade"),
    [
        ('{"grade": "correct", "reason": "ok"}', "correct"),
        ('{"grade": "partial", "reason": "ok"}', "partial"),
        ('{"grade": "great", "reason": "?"}', "invalid"),
        ("not json", "invalid"),
        ("[1]", "invalid"),
    ],
)
def test_parse_verdict(reply: str, grade: str) -> None:
    assert parse_verdict(reply).grade == grade


def test_the_real_question_set_is_well_formed() -> None:
    path = Path(__file__).resolve().parents[2] / "eval" / "questions.json"
    questions = load_questions(path)

    assert len({q.id for q in questions}) == len(questions)  # unique ids
    assert sum(not q.answerable for q in questions) >= 3  # some "I don't know" questions
    assert all(
        u.startswith("https://www.stavanger.kommune.no/")
        for q in questions
        for u in q.expected_urls
    )


class FakeRetriever:
    def retrieve(self, question: str, top_k: int) -> list[ScoredChunk]:
        return [hit("https://x.no/a/")]


class ScriptedGenerator:
    """Returns a pre-set answer per question."""

    def __init__(self, answers: dict[str, Answer]) -> None:
        self.answers = answers

    def answer(self, question: str, context: Sequence[ScoredChunk]) -> Answer:
        return self.answers[question]


class FakeJudge:
    def __init__(self) -> None:
        self.calls: list[str] = []

    def judge(self, question: str, reference: str, answer: str) -> Verdict:
        self.calls.append(question)
        return Verdict("partial", "judged")


def test_evaluate_answers_grades_refusals_without_the_judge() -> None:
    a_chunk = hit("https://x.no/a/").chunk
    questions = [
        EvalQuestion("q1", "no", "answered", ("https://x.no/a/",), "ref"),
        EvalQuestion("q2", "no", "refused", ("https://x.no/a/",), "ref"),
        EvalQuestion("q3", "no", "off-topic", (), "ref"),
    ]
    generator = ScriptedGenerator({
        "answered": Answer("svar [1]", [a_chunk]),
        "refused": Answer("Jeg vet ikke.", []),
        "off-topic": Answer("Jeg vet ikke.", []),
    })  # fmt: skip
    judge = FakeJudge()

    results = evaluate_answers(ChatService(FakeRetriever(), generator), judge, questions)

    assert [(r.question_id, r.grade) for r in results] == [
        ("q1", "partial"),  # from the judge
        ("q2", "incorrect"),  # answerable, but it said it didn't know
        ("q3", "correct"),  # unanswerable, and it said it didn't know
    ]
    assert judge.calls == ["answered"]  # the judge is only needed for real answers
    assert [r.cited_expected_page for r in results] == [True, False, False]
