"""Integration tests against the real Postgres from docker-compose.yml.

Skipped automatically when the database isn't running, so `pytest` still works offline.
They use their own schema (python_test), dropped afterwards.
"""

from collections.abc import Iterator

import psycopg
import pytest
from fakes import DIMENSIONS, embed_words

from ragbot.config import Settings
from ragbot.domain import Chunk
from ragbot.indexing.adapters.pgvector_index import PgVectorChunkIndex

SCHEMA = "python_test"


def database_available(conninfo: str) -> bool:
    try:
        with psycopg.connect(conninfo, connect_timeout=2):
            return True
    except psycopg.OperationalError:
        return False


CONNINFO = Settings.load().postgres_conninfo
pytestmark = pytest.mark.skipif(  # applies to every test in this file
    not database_available(CONNINFO), reason="Postgres not running (docker compose up -d)"
)


@pytest.fixture
def index() -> Iterator[PgVectorChunkIndex]:
    index = PgVectorChunkIndex(CONNINFO, SCHEMA, DIMENSIONS)
    index.clear()
    yield index  # the test runs here; the code below is the teardown (≈ Dispose)
    with psycopg.connect(CONNINFO) as conn:
        conn.execute(f"DROP SCHEMA IF EXISTS {SCHEMA} CASCADE")


def chunk(url: str, position: int, text: str) -> Chunk:
    return Chunk(id=f"{url}#{position}", document_url=url, document_title="T", text=text,
                 position=position, heading="H", metadata={"chunker": "test"})  # fmt: skip


def test_replace_search_and_delete(index: PgVectorChunkIndex) -> None:
    glass = [chunk("https://x.no/glass/", 0, "glass og metall")]
    paint = [chunk("https://x.no/maling/", 0, "maling er farlig avfall"),
             chunk("https://x.no/maling/", 1, "lever maling på stasjonen")]  # fmt: skip
    index.replace_document("https://x.no/glass/", "v1", glass, [embed_words(c.text) for c in glass])
    index.replace_document(
        "https://x.no/maling/", "v1", paint, [embed_words(c.text) for c in paint]
    )

    results = index.search(embed_words("farlig maling"), top_k=2)

    assert [r.chunk.document_url for r in results] == ["https://x.no/maling/"] * 2
    assert results[0].chunk == paint[0]  # round-trips every field, incl. heading and metadata
    assert 0 < results[1].score < results[0].score <= 1

    index.replace_document("https://x.no/maling/", "v2", paint[:1], [embed_words(paint[0].text)])
    assert index.document_versions() == {"https://x.no/glass/": "v1", "https://x.no/maling/": "v2"}

    index.delete_document("https://x.no/glass/")
    assert set(index.document_versions()) == {"https://x.no/maling/"}


def test_keyword_search_is_norwegian_full_text(index: PgVectorChunkIndex) -> None:
    chunks = [
        chunk("https://x.no/a/", 0, "Batterier er farlig avfall"),
        chunk("https://x.no/a/", 1, "Glass og metall leveres i glassbeholderen"),
        chunk("https://x.no/a/", 2, "Batterier og maling leveres på gjenvinningsstasjonen"),
    ]
    index.replace_document("https://x.no/a/", "v1", chunks, [embed_words(c.text) for c in chunks])

    # "batteriene" is stemmed to "batteri" and matches "Batterier"; "hvor"/"jeg" are stopwords.
    # OR semantics: chunks with either word match, the one with both words ranks first.
    results = index.keyword_search("Hvor leverer jeg batteriene?", top_k=5)

    assert [r.chunk.position for r in results] == [2, 0, 1]
    assert index.keyword_search("hvor er jeg", top_k=5) == []  # only stopwords: no query


def test_wrong_vector_size_asks_for_rebuild(index: PgVectorChunkIndex) -> None:
    index.document_versions()  # creates the table with DIMENSIONS
    other = PgVectorChunkIndex(CONNINFO, SCHEMA, DIMENSIONS * 2)

    with pytest.raises(RuntimeError, match="--rebuild"):
        other.document_versions()

    other.clear()  # --rebuild path: recreates the table with the new size
    assert other.document_versions() == {}
