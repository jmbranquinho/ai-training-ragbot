from datetime import UTC, datetime

from ragbot.domain import Document
from ragbot.indexing.deduplicate import deduplicate


def doc(url: str, content: str) -> Document:
    return Document(
        url=url, title=url, content=content, fetched_at=datetime(2026, 1, 1, tzinfo=UTC)
    )


def test_keeps_one_page_per_content_with_the_shortest_url() -> None:
    pages = [
        doc("https://x.no/plan/kapittel-1/", "hele planen"),
        doc("https://x.no/annet/", "noe annet"),
        doc("https://x.no/plan/", "hele planen"),
        doc("https://x.no/plan/kapittel-2/", "hele planen"),
    ]

    result = deduplicate(pages)

    assert [d.url for d in result] == ["https://x.no/plan/", "https://x.no/annet/"]


def test_equal_length_urls_pick_the_alphabetically_first() -> None:
    result = deduplicate([doc("https://x.no/b/", "same"), doc("https://x.no/a/", "same")])

    assert [d.url for d in result] == ["https://x.no/a/"]
