"""ChunkIndex kept in memory: brute-force cosine similarity. For tests and experiments."""

import math
from collections.abc import Sequence

from ragbot.domain import Chunk, ScoredChunk
from ragbot.indexing.ports import Vector


class InMemoryChunkIndex:
    def __init__(self) -> None:
        # url → (version, [(chunk, vector), ...])
        self._documents: dict[str, tuple[str, list[tuple[Chunk, Vector]]]] = {}

    def document_versions(self) -> dict[str, str]:
        return {url: version for url, (version, _) in self._documents.items()}

    def replace_document(
        self, document_url: str, version: str, chunks: Sequence[Chunk], vectors: Sequence[Vector]
    ) -> None:
        if len(chunks) != len(vectors):
            raise ValueError("need exactly one vector per chunk")
        self._documents[document_url] = (version, list(zip(chunks, vectors, strict=True)))

    def delete_document(self, document_url: str) -> None:
        self._documents.pop(document_url, None)

    def clear(self) -> None:
        self._documents.clear()

    def search(self, query_vector: Vector, top_k: int) -> list[ScoredChunk]:
        scored = [
            ScoredChunk(chunk, cosine_similarity(query_vector, vector))
            for _, entries in self._documents.values()
            for chunk, vector in entries
        ]
        return sorted(scored, key=lambda s: s.score, reverse=True)[:top_k]


def cosine_similarity(a: Vector, b: Vector) -> float:
    dot = sum(x * y for x, y in zip(a, b, strict=True))
    norm = math.sqrt(sum(x * x for x in a)) * math.sqrt(sum(y * y for y in b))
    return dot / norm if norm else 0.0
