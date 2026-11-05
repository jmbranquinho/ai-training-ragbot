"""Retrieval metrics: pure functions, no I/O.

- rank:   position (1-based) of the first retrieved chunk that comes from a correct page.
- hit@k:  share of questions with rank <= k ("is the right page among the first k?").
- MRR:    mean of 1/rank (0 when not found). 1.0 = always first, 0.5 = typically second.
"""

from collections.abc import Sequence
from dataclasses import dataclass

from ragbot.domain import ScoredChunk


def first_hit_rank(results: Sequence[ScoredChunk], expected_urls: Sequence[str]) -> int | None:
    for rank, hit in enumerate(results, start=1):
        if hit.chunk.document_url in expected_urls:
            return rank
    return None


@dataclass(frozen=True)
class RetrievalSummary:
    questions: int
    hit_at: dict[int, float]  # k → share of questions
    mrr: float
    not_found: int  # questions whose page wasn't in the results at all
    worst_rank: int | None  # largest rank among found ones: "how deep must we go?"


def summarize(ranks: Sequence[int | None], ks: Sequence[int]) -> RetrievalSummary:
    if not ranks:
        raise ValueError("no ranks to summarize")
    found = [r for r in ranks if r is not None]
    return RetrievalSummary(
        questions=len(ranks),
        hit_at={k: sum(1 for r in found if r <= k) / len(ranks) for k in ks},
        mrr=sum(1 / r for r in found) / len(ranks),
        not_found=len(ranks) - len(found),
        worst_rank=max(found) if found else None,
    )
