from datetime import UTC, datetime

from fakes import FakeEmbedder, embed_words

from ragbot.domain import Document
from ragbot.indexing.adapters.in_memory_index import InMemoryChunkIndex
from ragbot.indexing.chunkers import HeadingChunker
from ragbot.indexing.service import IndexService
from ragbot.ingestion.adapters.in_memory_store import InMemoryDocumentStore

GLASS = "https://x.no/glass/"
BATTERY = "https://x.no/batterier/"


def doc(url: str, content: str) -> Document:
    return Document(
        url=url, title=url, content=content, fetched_at=datetime(2026, 1, 1, tzinfo=UTC)
    )


def setup() -> tuple[InMemoryDocumentStore, FakeEmbedder, InMemoryChunkIndex]:
    store = InMemoryDocumentStore()
    store.save(doc(GLASS, "## Glass\n\nGlass og metall leveres i glassbeholderen."))
    store.save(doc(BATTERY, "## Batterier\n\nBatterier er farlig avfall.\n\n## Maling\n\nOgså."))
    return store, FakeEmbedder(), InMemoryChunkIndex()


def test_first_run_embeds_and_indexes_every_page() -> None:
    store, embedder, index = setup()

    report = IndexService(store, HeadingChunker(), embedder, index).run()

    assert sorted(report.indexed) == [BATTERY, GLASS]
    assert report.chunks_embedded == 3
    assert index.document_versions()[GLASS].startswith("headings:1500|")


def test_second_run_skips_unchanged_pages_without_calling_the_embedder() -> None:
    store, embedder, index = setup()
    IndexService(store, HeadingChunker(), embedder, index).run()
    embedder.calls.clear()

    store.save(doc(GLASS, "## Glass\n\nNy tekst om glass."))  # one page changed
    report = IndexService(store, HeadingChunker(), embedder, index).run()

    assert report.indexed == [GLASS]
    assert report.unchanged == [BATTERY]
    assert len(embedder.calls) == 1


def test_changing_chunker_settings_reindexes_everything() -> None:
    store, embedder, index = setup()
    IndexService(store, HeadingChunker(), embedder, index).run()

    report = IndexService(store, HeadingChunker(max_chars=800), embedder, index).run()

    assert sorted(report.indexed) == [BATTERY, GLASS]


def test_pages_that_became_duplicates_are_removed_from_the_index() -> None:
    store, embedder, index = setup()
    IndexService(store, HeadingChunker(), embedder, index).run()

    # same content as GLASS under a longer URL: dedup keeps GLASS, so nothing new is indexed
    store.save(
        doc("https://x.no/glass/kopi/", "## Glass\n\nGlass og metall leveres i glassbeholderen.")
    )
    store.save(doc(BATTERY, "## Glass\n\nGlass og metall leveres i glassbeholderen."))
    report = IndexService(store, HeadingChunker(), embedder, index).run()

    assert report.deleted == [BATTERY]  # BATTERY now duplicates GLASS (shorter URL wins)
    assert set(index.document_versions()) == {GLASS}


def test_search_ranks_the_most_similar_chunk_first() -> None:
    store, embedder, index = setup()
    IndexService(store, HeadingChunker(), embedder, index).run()

    results = index.search(embed_words("hvor leverer jeg glass og metall"), top_k=2)

    assert results[0].chunk.document_url == GLASS
    assert results[0].score > results[1].score
