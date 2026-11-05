"""Regenerates the shared prompt fixtures in testdata/ after an intentional prompt change.

Run from the python folder:  uv run python tests/make_fixtures.py
Then run both test suites: the .NET prompts must produce exactly the same text.
"""

from pathlib import Path

from chat_fixtures import CONTEXT, QUESTION, render

from ragbot.chat.prompt import build_messages
from ragbot.evaluation.judge import build_judge_messages

TESTDATA = Path(__file__).resolve().parents[2] / "testdata"

if __name__ == "__main__":
    fixtures = {
        TESTDATA / "chat" / "expected-prompt.txt": build_messages(QUESTION, CONTEXT),
        TESTDATA / "eval" / "expected-judge-prompt.txt": build_judge_messages(
            "Spørsmål?", "Fasit.", "Svar [1]."
        ),
    }
    for path, messages in fixtures.items():
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(render(messages).encode("utf-8"))  # bytes: LF line endings
        print(f"wrote {path}")
