"""Domain models.

`@dataclass(frozen=True)` is Python's closest match to a C# `record`:
value equality, a generated constructor, and immutable fields.
"""

import hashlib
from dataclasses import dataclass, field
from datetime import date, datetime


@dataclass(frozen=True)
class Document:
    """A single page fetched from the source website, converted to markdown."""

    url: str
    title: str
    content: str  # markdown
    fetched_at: datetime
    section: str | None = None  # `str | None` ≈ `string?` in C#
    updated_on: date | None = None  # "last updated" date shown on the page, if any

    @property
    def content_hash(self) -> str:
        """SHA-256 of the content. Used to detect whether a page changed since last run.

        The site sends no ETag/Last-Modified headers, so hashing what we extracted is the
        most reliable change signal we have.
        """
        return hashlib.sha256(self.content.encode("utf-8")).hexdigest()


@dataclass(frozen=True)
class Chunk:
    """A piece of a Document. Keeps a pointer back to its source for citations."""

    id: str  # "<document url>#<position>": stable, so re-indexing can overwrite
    document_url: str
    document_title: str
    text: str  # what gets embedded: a context header line + the chunk body
    position: int  # order of this chunk within its document
    heading: str | None = None  # heading path inside the page, e.g. "Levering > Gjenbruk"
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
