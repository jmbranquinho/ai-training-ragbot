"""Chat tests. The .NET suite (ChatTests.cs) uses the same inputs and expectations."""

from collections.abc import Sequence
from pathlib import Path

import pytest
from chat_fixtures import CONTEXT, QUESTION, render

from ragbot.chat.prompt import build_messages, parse_answer
from ragbot.chat.service import NO_CONTEXT_ANSWER, ChatService
from ragbot.domain import Answer, ScoredChunk


def test_prompt_matches_the_shared_fixture(testdata_dir: Path) -> None:
    expected = (testdata_dir.parent / "chat" / "expected-prompt.txt").read_bytes().decode("utf-8")

    assert render(build_messages(QUESTION, CONTEXT)) == expected


def test_parse_answer_keeps_only_real_cited_sources() -> None:
    reply = '{"answer": "Lever den på stasjonen [1].", "sources": [2, 1, 2, 9]}'

    answer = parse_answer(reply, CONTEXT)

    assert answer.text == "Lever den på stasjonen [1]."
    # cited in the text first, then the rest of the JSON list; 9 doesn't exist
    assert [c.id for c in answer.sources] == [CONTEXT[0].chunk.id, CONTEXT[1].chunk.id]
    assert not answer.is_unknown


def test_parse_answer_renumbers_citations_to_match_the_source_list() -> None:
    reply = '{"answer": "Glass [2]. Maling [1][7].", "sources": [1]}'

    answer = parse_answer(reply, CONTEXT)

    assert answer.text == "Glass [1]. Maling [2]."  # [7] points at nothing: removed
    assert [c.id for c in answer.sources] == [CONTEXT[1].chunk.id, CONTEXT[0].chunk.id]


def test_parse_answer_without_sources_is_unknown() -> None:
    answer = parse_answer('{"answer": "Jeg vet ikke.", "sources": []}', CONTEXT)

    assert answer.text == "Jeg vet ikke."
    assert answer.is_unknown


def test_parse_answer_tolerates_a_code_fence() -> None:
    reply = '```json\n{"answer": "Ja [2].", "sources": [2]}\n```'

    assert [c.id for c in parse_answer(reply, CONTEXT).sources] == [CONTEXT[1].chunk.id]


@pytest.mark.parametrize(
    "reply",
    ["not json", "[1, 2]", '{"text": "missing answer"}', '{"answer": "x", "sources": "1"}'],
)
def test_unexpected_replies_are_unknown(reply: str) -> None:
    answer = parse_answer(reply, CONTEXT)

    assert answer.is_unknown
    assert answer.text == reply


class FakeRetriever:
    def __init__(self, results: list[ScoredChunk]) -> None:
        self.results = results
        self.calls: list[tuple[str, int]] = []

    def retrieve(self, question: str, top_k: int) -> list[ScoredChunk]:
        self.calls.append((question, top_k))
        return self.results[:top_k]


class FakeGenerator:
    def __init__(self) -> None:
        self.contexts: list[list[ScoredChunk]] = []

    def answer(self, question: str, context: Sequence[ScoredChunk]) -> Answer:
        self.contexts.append(list(context))
        return Answer(text="svar", sources=[context[0].chunk])


def test_chat_service_passes_top_k_context_to_the_generator() -> None:
    retriever, generator = FakeRetriever(CONTEXT), FakeGenerator()

    answer = ChatService(retriever, generator, top_k=1).ask("  Hvor leverer jeg maling?  ")

    assert retriever.calls == [("Hvor leverer jeg maling?", 1)]  # question trimmed
    assert generator.contexts == [CONTEXT[:1]]
    assert answer.text == "svar"


def test_chat_service_says_it_does_not_know_without_calling_the_model() -> None:
    generator = FakeGenerator()

    answer = ChatService(FakeRetriever([]), generator).ask("Hvem vant VM i fotball?")

    assert answer.is_unknown
    assert answer.text == NO_CONTEXT_ANSWER
    assert generator.contexts == []


def test_chat_service_rejects_an_empty_question() -> None:
    with pytest.raises(ValueError):
        ChatService(FakeRetriever(CONTEXT), FakeGenerator()).ask("   ")
