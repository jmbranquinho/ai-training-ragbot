"""The evaluation question set (eval/questions.json, shared with the .NET version)."""

import json
from dataclasses import dataclass
from pathlib import Path


@dataclass(frozen=True)
class EvalQuestion:
    id: str
    language: str
    question: str
    expected_urls: tuple[str, ...]  # pages that contain the answer; any one counts
    reference: str  # the key facts, for the answer judge

    @property
    def answerable(self) -> bool:
        """False when the site doesn't answer it: then "I don't know" is the right answer."""
        return bool(self.expected_urls)


def load_questions(path: Path) -> list[EvalQuestion]:
    data = json.loads(path.read_text(encoding="utf-8"))
    return [
        EvalQuestion(
            id=q["id"],
            language=q["language"],
            question=q["question"],
            expected_urls=tuple(q["expected_urls"]),
            reference=q["reference"],
        )
        for q in data["questions"]
    ]
