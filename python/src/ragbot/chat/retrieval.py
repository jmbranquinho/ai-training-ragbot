"""Retrievers: find the chunks most relevant to a question. All satisfy the Retriever port.

- VectorRetriever:  semantic similarity (embeddings). Handles synonyms, paraphrases and
                    other languages, but can miss exact terms such as product names or codes.
- KeywordRetriever: full-text search. Exact words, but no understanding of meaning.
- HybridRetriever:  both, merged with Reciprocal Rank Fusion. Usually the most robust.
"""

from collections.abc import Sequence

from ragbot.domain import Chunk, ScoredChunk
from ragbot.indexing.ports import ChunkIndex, Embedder


class VectorRetriever:
    def __init__(self, embedder: Embedder, index: ChunkIndex) -> None:
        self._embedder = embedder
        self._index = index

    def retrieve(self, question: str, top_k: int) -> list[ScoredChunk]:
        query_vector = self._embedder.embed([question])[0]
        return self._index.search(query_vector, top_k)


class KeywordRetriever:
    def __init__(self, index: ChunkIndex) -> None:
        self._index = index

    def retrieve(self, question: str, top_k: int) -> list[ScoredChunk]:
        return self._index.keyword_search(question, top_k)


class HybridRetriever:
    """Runs vector and keyword search, then fuses the two rankings.

    Each search returns `candidates` hits (at least top_k), so a chunk ranked e.g. 12th by
    one method and 3rd by the other can still make the final top_k.
    """

    def __init__(
        self, embedder: Embedder, index: ChunkIndex, candidates: int = 20, rrf_k: int = 60
    ) -> None:
        self._vector = VectorRetriever(embedder, index)
        self._keyword = KeywordRetriever(index)
        self._candidates = candidates
        self._rrf_k = rrf_k

    def retrieve(self, question: str, top_k: int) -> list[ScoredChunk]:
        n = max(self._candidates, top_k)
        rankings = [self._vector.retrieve(question, n), self._keyword.retrieve(question, n)]
        return reciprocal_rank_fusion(rankings, self._rrf_k)[:top_k]


def reciprocal_rank_fusion(
    rankings: Sequence[Sequence[ScoredChunk]], k: int = 60
) -> list[ScoredChunk]:
    """Merge rankings by position only: score(chunk) = sum over lists of 1 / (k + rank).

    Raw scores are ignored on purpose: cosine similarity and full-text rank are on different
    scales. k = 60 is the value from the original RRF paper (Cormack et al., 2009); it keeps
    one list's top hit from dominating. Ties are broken by chunk id, so results are stable.
    """
    scores: dict[str, float] = {}
    chunks: dict[str, Chunk] = {}
    for ranking in rankings:
        for rank, hit in enumerate(ranking, start=1):
            scores[hit.chunk.id] = scores.get(hit.chunk.id, 0.0) + 1.0 / (k + rank)
            chunks[hit.chunk.id] = hit.chunk

    ordered = sorted(scores, key=lambda chunk_id: (-scores[chunk_id], chunk_id))
    return [ScoredChunk(chunks[chunk_id], scores[chunk_id]) for chunk_id in ordered]
