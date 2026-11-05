"""Interfaces for the chat feature.

The LLM call lives behind `AnswerGenerator`: the chat logic only knows it needs
"something that turns a question + context into an Answer". The Azure OpenAI
client will be an adapter that implements it (Step 7).
"""

from collections.abc import Sequence
from typing import Protocol

from ragbot.domain import Answer, ScoredChunk


class Retriever(Protocol):
    def retrieve(self, question: str, top_k: int) -> list[ScoredChunk]: ...


class AnswerGenerator(Protocol):
    def answer(self, question: str, context: Sequence[ScoredChunk]) -> Answer: ...
