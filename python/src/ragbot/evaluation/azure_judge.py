"""AnswerJudge backed by an Azure OpenAI chat deployment."""

from typing import Any

from openai import AzureOpenAI

from ragbot.evaluation.judge import Verdict, build_judge_messages, parse_verdict


class AzureOpenAIJudge:
    def __init__(self, client: AzureOpenAI, deployment: str) -> None:
        self._client = client
        self._deployment = deployment

    def judge(self, question: str, reference: str, answer: str) -> Verdict:
        messages: Any = build_judge_messages(question, reference, answer)
        response = self._client.chat.completions.create(
            model=self._deployment,
            messages=messages,
            temperature=0,
            response_format={"type": "json_object"},
        )
        return parse_verdict(response.choices[0].message.content or "")
