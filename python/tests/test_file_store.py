"""`tmp_path` is a built-in pytest fixture: a fresh temporary folder per test."""

import shutil
from datetime import UTC, date, datetime
from pathlib import Path

from ragbot.domain import Document
from ragbot.ingestion.adapters.file_store import FileDocumentStore
from ragbot.ingestion.ports import DocumentStore

URL = "https://www.stavanger.kommune.no/renovasjon-og-miljo/farlig-avfall/"

# The same document as testdata/stavanger/stored-document.md
FIXTURE_DOC = Document(
    url=URL,
    title="Farlig avfall: slik leverer du det",
    content="# Farlig avfall\n\nLegg batterier i den **røde** beholderen.\n\n- maling\n- lakk",
    fetched_at=datetime(2026, 11, 5, 9, 30, tzinfo=UTC),
    section="renovasjon-og-miljo",
    updated_on=date(2025, 10, 22),
)


def test_path_mirrors_the_url(tmp_path: Path) -> None:
    store = FileDocumentStore(tmp_path)

    assert store.path_for(URL) == (
        tmp_path / "www.stavanger.kommune.no" / "renovasjon-og-miljo" / "farlig-avfall.md"
    )
    assert store.path_for("https://example.com/") == tmp_path / "example.com" / "index.md"


def test_saved_file_matches_the_shared_format(tmp_path: Path, testdata_dir: Path) -> None:
    store = FileDocumentStore(tmp_path)

    store.save(FIXTURE_DOC)

    written = store.path_for(URL).read_bytes()
    assert written == (testdata_dir / "stored-document.md").read_bytes()
    assert list(tmp_path.rglob("*.tmp")) == []  # temp file was swapped in, not left behind


def test_reads_a_file_in_the_shared_format(tmp_path: Path, testdata_dir: Path) -> None:
    store = FileDocumentStore(tmp_path)
    store.path_for(URL).parent.mkdir(parents=True)
    shutil.copy(testdata_dir / "stored-document.md", store.path_for(URL))

    assert store.get(URL) == FIXTURE_DOC


def test_round_trip_without_optional_fields(tmp_path: Path) -> None:
    store: DocumentStore = FileDocumentStore(tmp_path)
    doc = Document(url="https://example.com/a/b/", title="", content="Hei",
                   fetched_at=datetime(2026, 1, 1, tzinfo=UTC))  # fmt: skip

    store.save(doc)

    assert store.get(doc.url) == doc


def test_missing_url_returns_none(tmp_path: Path) -> None:
    assert FileDocumentStore(tmp_path).get(URL) is None


def test_all_returns_every_saved_document(tmp_path: Path) -> None:
    store = FileDocumentStore(tmp_path)
    other = Document(url="https://example.com/x/", title="X", content="x",
                     fetched_at=datetime(2026, 1, 1, tzinfo=UTC))  # fmt: skip
    store.save(FIXTURE_DOC)
    store.save(other)
    store.save(FIXTURE_DOC)  # overwrite, not duplicate

    assert sorted(d.url for d in store.all()) == sorted([URL, other.url])
