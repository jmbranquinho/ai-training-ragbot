"""pytest finds any function named test_* in files named test_*.py (≈ [Fact] in xUnit)."""

from datetime import UTC, datetime

from ragbot.domain import Document
from ragbot.ingestion.adapters.in_memory_store import InMemoryDocumentStore
from ragbot.ingestion.ports import DocumentStore


def make_document(url: str = "https://example.com/a") -> Document:
    return Document(url=url, title="A", content="# Hello", fetched_at=datetime.now(UTC))


def test_saved_document_can_be_retrieved_by_url() -> None:
    store: DocumentStore = InMemoryDocumentStore()  # pyright checks it fits the protocol
    doc = make_document()

    store.save(doc)

    assert store.get(doc.url) == doc  # plain assert ≈ Assert.Equal


def test_unknown_url_returns_none() -> None:
    assert InMemoryDocumentStore().get("https://nope") is None


def test_saving_same_url_twice_overwrites() -> None:
    store = InMemoryDocumentStore()
    store.save(make_document())
    store.save(make_document())

    assert len(list(store.all())) == 1
