"""Interfaces for the indexing feature.

`ChunkIndex` is the contract shared by the offline pipeline (which writes to it)
and the chatbot (which searches it). That is the only thing the two sides share.
"""

from collections.abc import Sequence
from typing import Protocol

from ragbot.domain import Chunk, Document, ScoredChunk

type Vector = list[float]  # type alias (≈ `using Vector = float[];`)


class Chunker(Protocol):
    @property
    def signature(self) -> str:
        """Strategy + settings, e.g. "headings:1500". Changing it forces re-indexing."""
        ...

    def chunk(self, document: Document) -> list[Chunk]: ...


class Embedder(Protocol):
    def embed(self, texts: Sequence[str]) -> list[Vector]:
        """One vector per text, in the same order."""
        ...


class ChunkIndex(Protocol):
    def document_versions(self) -> dict[str, str]:
        """Version of every indexed document, by URL. Lets indexing skip unchanged pages."""
        ...

    def replace_document(
        self,
        document_url: str,
        version: str,
        chunks: Sequence[Chunk],
        vectors: Sequence[Vector],
    ) -> None:
        """Replace all chunks of one document (its chunk count may have changed)."""
        ...

    def delete_document(self, document_url: str) -> None: ...

    def clear(self) -> None:
        """Remove everything, e.g. after changing the embedding size."""
        ...

    def search(self, query_vector: Vector, top_k: int) -> list[ScoredChunk]:
        """The top_k chunks most similar to the query vector, best first (cosine similarity)."""
        ...

    def keyword_search(self, query: str, top_k: int) -> list[ScoredChunk]:
        """The top_k chunks matching the query's words, best first.

        Scores are on a different scale than search(), so only compare ranks across the two.
        """
        ...
