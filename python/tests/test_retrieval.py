"""Retriever tests. The .NET suite (RetrievalTests.cs) uses the same inputs and expectations."""

from fakes import FakeEmbedder, embed_words

from ragbot.chat.ports import Retriever
from ragbot.chat.retrieval import (
    HybridRetriever,
    KeywordRetriever,
    VectorRetriever,
    reciprocal_rank_fusion,
)
from ragbot.domain import Chunk, ScoredChunk
from ragbot.indexing.adapters.in_memory_index import InMemoryChunkIndex


def chunk(chunk_id: str, text: str = "") -> Chunk:
    return Chunk(id=chunk_id, document_url=f"https://x.no/{chunk_id}/", document_title=chunk_id,
                 text=text or chunk_id, position=0)  # fmt: skip


def ranking(*ids: str) -> list[ScoredChunk]:
    return [ScoredChunk(chunk(i), 1.0) for i in ids]


def test_rrf_rewards_chunks_ranked_well_by_both_lists() -> None:
    fused = reciprocal_rank_fusion([ranking("a", "b", "c"), ranking("c", "a", "d")], k=60)

    assert [s.chunk.id for s in fused] == ["a", "c", "b", "d"]
    assert fused[0].score == 1 / 61 + 1 / 62  # rank 1 in the first list, rank 2 in the second
    assert fused[2].score == 1 / 62  # only in the first list, rank 2


def test_rrf_breaks_ties_by_chunk_id() -> None:
    fused = reciprocal_rank_fusion([ranking("z"), ranking("y")])

    assert [s.chunk.id for s in fused] == ["y", "z"]


def make_index() -> InMemoryChunkIndex:
    index = InMemoryChunkIndex()
    texts = {
        "glass": "Glass og metall leveres i glassbeholderen.",
        "maling": "Maling og batterier er farlig avfall.",
        "brikke": "Bestill brikke til nedgravd container på nett.",
    }
    for chunk_id, text in texts.items():
        index.replace_document(f"https://x.no/{chunk_id}/", "v1", [chunk(chunk_id, text)],
                               [embed_words(text)])  # fmt: skip
    return index


def test_vector_and_keyword_retrievers_use_the_index() -> None:
    index = make_index()
    vector: Retriever = VectorRetriever(FakeEmbedder(), index)
    keyword: Retriever = KeywordRetriever(index)

    assert vector.retrieve("hvor leveres glass", top_k=1)[0].chunk.id == "glass"
    assert [s.chunk.id for s in keyword.retrieve("brikke container", top_k=5)] == ["brikke"]


def test_hybrid_retriever_returns_top_k_fused_results() -> None:
    retriever = HybridRetriever(FakeEmbedder(), make_index(), candidates=3)

    results = retriever.retrieve("farlig avfall maling", top_k=2)

    assert len(results) == 2
    assert results[0].chunk.id == "maling"  # first in both lists
    assert results[0].score > results[1].score
