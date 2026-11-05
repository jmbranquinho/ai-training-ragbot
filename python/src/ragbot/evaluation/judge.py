"""LLM-as-judge: grades an answer against the reference facts. Prompt + parsing are pure;
the model call is in azure_judge.py. The prompt text is pinned by
testdata/eval/expected-judge-prompt.txt, shared with the .NET version.

Limitation: the judge is the same model that wrote the answers, so it may be lenient with
its own style. A stronger or different model as judge would be more trustworthy.
"""

import json
from dataclasses import dataclass
from typing import Literal, Protocol

type Grade = Literal["correct", "partial", "incorrect", "invalid"]

JUDGE_SYSTEM_PROMPT = """\
You grade answers from a chatbot for the website of Stavanger kommune.
Compare the ANSWER with the REFERENCE, which holds the key facts from the website.
The answer may be in a different language than the reference; that is fine.

Grades:
- correct: contains the key facts of the reference and nothing that contradicts it.
- partial: misses some key facts, but contradicts nothing.
- incorrect: contradicts the reference, misses the main point, or says it doesn't know.
Only judge whether the key facts of the reference are present and not contradicted. Do NOT \
lower the grade because the answer contains more details than the reference: extra details \
are expected, since the answer comes from the full web page.

Reply with JSON only: {"grade": "correct" | "partial" | "incorrect", \
"reason": "<one short sentence>"}"""


@dataclass(frozen=True)
class Verdict:
    grade: Grade
    reason: str


class AnswerJudge(Protocol):
    def judge(self, question: str, reference: str, answer: str) -> Verdict: ...


def build_judge_messages(question: str, reference: str, answer: str) -> list[dict[str, str]]:
    user = f"QUESTION: {question}\n\nREFERENCE: {reference}\n\nANSWER: {answer}"
    return [{"role": "system", "content": JUDGE_SYSTEM_PROMPT}, {"role": "user", "content": user}]


def parse_verdict(reply: str) -> Verdict:
    try:
        data = json.loads(reply)
    except ValueError:
        return Verdict("invalid", reply.strip())
    grade = data.get("grade") if isinstance(data, dict) else None
    reason = data.get("reason", "") if isinstance(data, dict) else ""
    if grade not in ("correct", "partial", "incorrect"):
        return Verdict("invalid", reply.strip())
    return Verdict(grade, str(reason))
