"""Chunker tests. The .NET suite (ChunkerTests.cs) uses the same inputs and expectations."""

import itertools
from datetime import UTC, datetime

import pytest

from ragbot.domain import Document
from ragbot.indexing.chunkers import HeadingChunker, SlidingWindowChunker
from ragbot.indexing.ports import Chunker

URL = "https://example.com/avfall/"
MARKDOWN = """# Farlig avfall

Kort intro.

## Hva er farlig avfall?

Batterier og maling.

### Trygg levering

Bruk originalemballasjen.

## Kontaktinformasjon

## Gjenvinning

Lever på stasjonen."""


def make_doc(content: str = MARKDOWN) -> Document:
    return Document(
        url=URL,
        title="Farlig avfall",
        content=content,
        fetched_at=datetime(2026, 1, 1, tzinfo=UTC),
        section="avfall",
    )


def test_heading_chunker_makes_one_chunk_per_section() -> None:
    chunker: Chunker = HeadingChunker()

    chunks = chunker.chunk(make_doc())

    assert [c.heading for c in chunks] == [
        None,  # text under the h1, which is just the page title
        "Hva er farlig avfall?",
        "Hva er farlig avfall? > Trygg levering",  # nested heading keeps its parent
        "Gjenvinning",  # "Kontaktinformasjon" has no body, so it is skipped
    ]
    assert chunks[2].text == "Farlig avfall > Hva er farlig avfall? > Trygg levering\n\n" + (
        "Bruk originalemballasjen."
    )


def test_chunks_carry_metadata_for_citations() -> None:
    chunk = HeadingChunker().chunk(make_doc())[1]

    assert chunk.id == f"{URL}#1"
    assert chunk.position == 1
    assert chunk.document_url == URL
    assert chunk.document_title == "Farlig avfall"
    assert chunk.metadata == {"chunker": "headings", "section": "avfall"}


def test_heading_chunker_splits_long_sections_at_paragraphs() -> None:
    paragraphs = [f"Avsnitt {i} " + "x" * 80 for i in range(5)]  # 5 paragraphs of ~90 chars
    doc = make_doc("## Lang seksjon\n\n" + "\n\n".join(paragraphs))

    chunks = HeadingChunker(max_chars=200).chunk(doc)

    bodies = [c.text.split("\n\n", 1)[1] for c in chunks]  # drop the header line
    assert bodies == [
        f"{paragraphs[0]}\n\n{paragraphs[1]}",
        f"{paragraphs[2]}\n\n{paragraphs[3]}",
        paragraphs[4],
    ]
    assert all(c.heading == "Lang seksjon" for c in chunks)


def test_heading_chunker_hard_splits_a_too_long_paragraph_at_a_space() -> None:
    words = " ".join(f"ord{i:03}" for i in range(60))  # 60 words of 6 chars = 419 chars

    chunks = HeadingChunker(max_chars=150).chunk(make_doc(words))

    bodies = [c.text.split("\n\n", 1)[1] for c in chunks]
    assert all(len(b) <= 150 for b in bodies)
    assert " ".join(bodies) == words  # nothing lost, nothing duplicated
    assert all(not b.startswith(" ") and not b.endswith(" ") for b in bodies)


def test_sliding_window_overlaps_and_cuts_at_words() -> None:
    words = " ".join(f"ord{i:03}" for i in range(100))  # 699 chars

    chunks = SlidingWindowChunker(size=200, overlap=50).chunk(make_doc(words))

    bodies = [c.text.split("\n\n", 1)[1] for c in chunks]
    assert len(bodies) == 5
    assert all(len(b) <= 200 for b in bodies)
    assert all(b.startswith("ord") and b[-1].isdigit() for b in bodies)  # whole words only
    for first, second in itertools.pairwise(bodies):  # neighbours overlap
        assert first.split()[-1] in second.split()[:9]
    assert bodies[0].startswith("ord000") and bodies[-1].endswith("ord099")
    assert all(c.heading is None and c.metadata["chunker"] == "window" for c in chunks)


def test_short_document_is_one_window() -> None:
    chunks = SlidingWindowChunker().chunk(make_doc("Kort tekst."))

    assert [c.text for c in chunks] == ["Farlig avfall\n\nKort tekst."]


@pytest.mark.parametrize(("size", "overlap"), [(50, 0), (200, 100), (200, -1)])
def test_sliding_window_rejects_bad_settings(size: int, overlap: int) -> None:
    with pytest.raises(ValueError):  # ≈ Assert.Throws<ArgumentException>
        SlidingWindowChunker(size=size, overlap=overlap)
