"""IngestService tests with hand-written fakes.

The fakes don't inherit from anything: they just have the right methods, which is all a
Protocol needs (pyright still checks them when they're passed to IngestService).
"""

from collections.abc import Iterable, Iterator
from datetime import UTC, datetime

from ragbot.domain import Document
from ragbot.ingestion.adapters.in_memory_store import InMemoryDocumentStore
from ragbot.ingestion.service import IngestService

FIRST_RUN = datetime(2026, 1, 1, tzinfo=UTC)


class FakeUrlSource:
    def __init__(self, urls: list[str]) -> None:
        self.urls = urls

    def discover(self) -> list[str]:
        return self.urls


class FakeScraper:
    """Returns whatever content is currently set for each URL."""

    def __init__(self, pages: dict[str, str], fetched_at: datetime) -> None:
        self.pages = pages
        self.fetched_at = fetched_at

    def fetch(self, urls: Iterable[str]) -> Iterator[Document]:
        for url in urls:
            yield Document(url=url, title=url, content=self.pages[url], fetched_at=self.fetched_at)


def test_first_run_stores_everything_as_new() -> None:
    store = InMemoryDocumentStore()
    scraper = FakeScraper({"a": "A", "b": "B"}, FIRST_RUN)

    report = IngestService(FakeUrlSource(["a", "b"]), scraper, store).run()

    assert report.new == ["a", "b"]
    assert report.updated == report.unchanged == []
    assert {d.url for d in store.all()} == {"a", "b"}


def test_second_run_detects_updated_and_unchanged_pages() -> None:
    store = InMemoryDocumentStore()
    source = FakeUrlSource(["a", "b"])
    scraper = FakeScraper({"a": "A", "b": "B"}, FIRST_RUN)
    IngestService(source, scraper, store).run()

    scraper.pages["b"] = "B, edited"  # the website changed one page
    scraper.fetched_at = datetime(2026, 2, 1, tzinfo=UTC)
    report = IngestService(source, scraper, store).run()

    assert report.new == []
    assert report.updated == ["b"]
    assert report.unchanged == ["a"]

    a, b = store.get("a"), store.get("b")
    assert a is not None and b is not None  # narrows `Document | None` to `Document`
    assert a.fetched_at == FIRST_RUN  # unchanged page was not rewritten
    assert b.content == "B, edited"
