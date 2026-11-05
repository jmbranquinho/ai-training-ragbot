"""Domain models.

`@dataclass(frozen=True)` is Python's closest match to a C# `record`:
value equality, a generated constructor, and immutable fields.
"""

from dataclasses import dataclass, field
from datetime import datetime


@dataclass(frozen=True)
class Document:
    """A single page fetched from the source website, converted to markdown."""

    url: str
    title: str
    content: str
    fetched_at: datetime
    section: str | None = None  # `str | None` ≈ `string?` in C#


@dataclass(frozen=True)
class Chunk:
    """A piece of a Document. Keeps a pointer back to its source for citations."""

    id: str
    document_url: str
    document_title: str
    text: str
    position: int  # order of this chunk within its document
    metadata: dict[str, str] = field(default_factory=dict)


@dataclass(frozen=True)
class ScoredChunk:
    """A Chunk returned by a search, with its similarity score (higher = more relevant)."""

    chunk: Chunk
    score: float


@dataclass(frozen=True)
class Answer:
    """The chatbot's reply. `sources` should point back to where the answer came from."""

    text: str
    sources: list[Chunk] = field(default_factory=list)

    @property
    def is_unknown(self) -> bool:
        """True when nothing relevant was found and the bot declined to answer."""
        return not self.sources
