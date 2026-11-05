"""Chunk by markdown headings: one chunk per section, so each chunk is about one topic.

Sections longer than `max_chars` are split further at paragraph breaks (blank lines),
and a single paragraph that is still too long is split at the last space before the limit.
`max_chars` limits the body; the context header is added on top.
"""

import re

from ragbot.domain import Chunk, Document
from ragbot.indexing.chunkers._common import last_whitespace, make_chunk

_HEADING = re.compile(r"^(#{1,6})\s+(.+?)\s*#*\s*$")  # "## Title" → level 2, "Title"


class HeadingChunker:
    name = "headings"

    def __init__(self, max_chars: int = 1500) -> None:
        if max_chars < 100:
            raise ValueError("max_chars must be at least 100")
        self._max_chars = max_chars

    @property  # ≈ a get-only C# property
    def signature(self) -> str:
        return f"{self.name}:{self._max_chars}"

    def chunk(self, document: Document) -> list[Chunk]:
        chunks: list[Chunk] = []
        for heading, body in _sections(document.content, document.title):
            for piece in _split_long(body, self._max_chars):
                chunks.append(make_chunk(document, len(chunks), heading, piece, self.name))
        return chunks


def _sections(markdown: str, title: str) -> list[tuple[str | None, str]]:
    """Split into (heading path, body) pairs. Sections without a body are dropped."""
    sections: list[tuple[str | None, str]] = []
    path: list[tuple[int, str]] = []  # stack of (level, heading text)
    lines: list[str] = []

    def flush() -> None:  # a nested function, like a C# local function
        body = "\n".join(lines).strip()
        if body:
            names = [text for _, text in path]
            if names and names[0] == title:  # the h1 repeats the page title: don't say it twice
                names = names[1:]
            sections.append((" > ".join(names) or None, body))
        lines.clear()

    for line in markdown.splitlines():
        match = _HEADING.match(line)
        if match is None:
            lines.append(line)
            continue
        flush()
        level = len(match.group(1))
        while path and path[-1][0] >= level:  # leaving deeper (or same-level) sections
            path.pop()
        path.append((level, match.group(2)))

    flush()
    return sections


def _split_long(text: str, max_chars: int) -> list[str]:
    if len(text) <= max_chars:
        return [text]

    pieces: list[str] = []
    current = ""
    for paragraph in (p.strip() for p in text.split("\n\n")):  # a generator expression
        if not paragraph:
            continue
        while len(paragraph) > max_chars:  # one paragraph alone is too long: hard split
            cut = last_whitespace(paragraph, 1, max_chars + 1)
            cut = cut if cut > 0 else max_chars
            if current:
                pieces.append(current)
                current = ""
            pieces.append(paragraph[:cut].rstrip())
            paragraph = paragraph[cut:].lstrip()

        candidate = f"{current}\n\n{paragraph}" if current else paragraph
        if len(candidate) <= max_chars:
            current = candidate
        else:
            pieces.append(current)
            current = paragraph

    if current:
        pieces.append(current)
    return pieces
