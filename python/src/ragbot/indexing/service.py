"""The indexing use case: stored pages → de-duplicate → chunk → embed → index.

Works page by page and skips pages whose version is already indexed. A version is
"<chunker signature>|<content hash>", so a changed page *or* changed chunker settings
re-embeds that page, while unchanged pages cost nothing.
"""

import logging
from dataclasses import dataclass, field

from ragbot.indexing.deduplicate import deduplicate
from ragbot.indexing.ports import Chunker, ChunkIndex, Embedder
from ragbot.ingestion.ports import DocumentStore

logger = logging.getLogger(__name__)


@dataclass
class IndexReport:
    indexed: list[str] = field(default_factory=list)
    unchanged: list[str] = field(default_factory=list)
    deleted: list[str] = field(default_factory=list)
    chunks_embedded: int = 0


class IndexService:
    def __init__(
        self, store: DocumentStore, chunker: Chunker, embedder: Embedder, index: ChunkIndex
    ) -> None:
        self._store = store
        self._chunker = chunker
        self._embedder = embedder
        self._index = index

    def run(self, rebuild: bool = False) -> IndexReport:
        report = IndexReport()
        if rebuild:
            self._index.clear()

        documents = deduplicate(self._store.all())
        indexed_versions = self._index.document_versions()

        for document in documents:
            version = f"{self._chunker.signature}|{document.content_hash}"
            if indexed_versions.get(document.url) == version:
                report.unchanged.append(document.url)
                continue

            chunks = self._chunker.chunk(document)
            vectors = self._embedder.embed([c.text for c in chunks])
            self._index.replace_document(document.url, version, chunks, vectors)
            report.indexed.append(document.url)
            report.chunks_embedded += len(chunks)
            logger.info("indexed  %3d chunks  %s", len(chunks), document.url)

        # Pages that disappeared from the store (or became duplicates) leave the index too
        current = {d.url for d in documents}
        for url in indexed_versions.keys() - current:  # set difference
            self._index.delete_document(url)
            report.deleted.append(url)
            logger.info("deleted  %s", url)

        return report
