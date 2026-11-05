"""The fixed example used for testdata/chat/expected-prompt.txt (same values in ChatTestData.cs)."""

from ragbot.domain import Chunk, ScoredChunk

QUESTION = "Hvor leverer jeg maling?"

CONTEXT = [
    ScoredChunk(
        Chunk(
            id="https://x.no/farlig-avfall/#0",
            document_url="https://x.no/farlig-avfall/",
            document_title="Farlig avfall",
            text="Farlig avfall > Levering\n\nLever maling på gjenvinningsstasjonen.",
            position=0,
            heading="Levering",
        ),
        0.9,
    ),
    ScoredChunk(
        Chunk(
            id="https://x.no/glass/#2",
            document_url="https://x.no/glass/",
            document_title="Glass",
            text="Glass > Henting\n\nGlass hentes hver fjerde uke.",
            position=2,
            heading="Henting",
        ),
        0.5,
    ),
]


def render(messages: list[dict[str, str]]) -> str:
    """Messages as plain text, the format of expected-prompt.txt."""
    return "".join(f"=== {m['role']} ===\n{m['content']}\n" for m in messages)
