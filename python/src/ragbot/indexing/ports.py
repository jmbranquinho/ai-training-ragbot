"""Interfaces for the indexing feature.

`ChunkIndex` is the contract shared by the offline pipeline (which writes to it)
and the chatbot (which searches it). That is the only thing the two sides share.
"""

from collections.abc import Sequence
from typing import Protocol

from ragbot.domain import Chunk, Document, ScoredChunk

type Vector = list[float]  # type alias (≈ `using Vector = float[];`)


class Chunker(Protocol):
    def chunk(self, document: Document) -> list[Chunk]: ...


class Embedder(Protocol):
    def embed(self, texts: Sequence[str]) -> list[Vector]: ...


class ChunkIndex(Protocol):
    def add(self, chunks: Sequence[Chunk], vectors: Sequence[Vector]) -> None: ...

    def search(self, query_vector: Vector, top_k: int) -> list[ScoredChunk]: ...
