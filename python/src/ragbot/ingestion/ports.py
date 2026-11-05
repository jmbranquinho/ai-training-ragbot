"""Interfaces ("ports") for the ingestion feature.

A `Protocol` is like a C# interface, but structural: any class with matching
methods satisfies it automatically, with no `: IScraper` needed. The type checker
(pyright) verifies the match.
"""

from collections.abc import Iterable, Iterator
from typing import Protocol

from ragbot.domain import Document


class Scraper(Protocol):
    def fetch(self, urls: Iterable[str]) -> Iterator[Document]:
        """Fetch pages and return them as Documents (≈ IEnumerable<Document> with yield)."""
        ...


class DocumentStore(Protocol):
    def save(self, document: Document) -> None: ...

    def get(self, url: str) -> Document | None: ...

    def all(self) -> Iterator[Document]: ...
