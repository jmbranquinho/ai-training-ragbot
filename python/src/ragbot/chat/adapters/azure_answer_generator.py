"""AnswerGenerator backed by an Azure OpenAI chat deployment (e.g. gpt-4.1-mini).

Thin on purpose: the prompt and the answer parsing live in chat/prompt.py, where they are
tested without calling the model.
"""

from collections.abc import Sequence
from typing import Any

from openai import AzureOpenAI

from ragbot.chat.prompt import build_messages, parse_answer
from ragbot.domain import Answer, ScoredChunk


class AzureOpenAIAnswerGenerator:
    def __init__(self, client: AzureOpenAI, deployment: str) -> None:
        self._client = client
        self._deployment = deployment

    def answer(self, question: str, context: Sequence[ScoredChunk]) -> Answer:
        messages: Any = build_messages(question, context)  # plain dicts are what the API takes
        response = self._client.chat.completions.create(
            model=self._deployment,
            messages=messages,
            temperature=0,  # as repeatable as possible
            response_format={"type": "json_object"},  # guarantees syntactically valid JSON
        )
        return parse_answer(response.choices[0].message.content or "", context)
