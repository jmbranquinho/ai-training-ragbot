"""In-memory DocumentStore, for development and tests. Swapped for a file store in Step 3."""

from collections.abc import Iterator

from ragbot.domain import Document


class InMemoryDocumentStore:
    """Satisfies the DocumentStore protocol without inheriting from it."""

    def __init__(self) -> None:  # ≈ constructor
        self._documents: dict[str, Document] = {}  # leading _ = "private" by convention

    def save(self, document: Document) -> None:
        self._documents[document.url] = document

    def get(self, url: str) -> Document | None:
        return self._documents.get(url)

    def all(self) -> Iterator[Document]:
        yield from self._documents.values()
