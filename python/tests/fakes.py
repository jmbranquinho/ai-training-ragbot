"""Test doubles shared by several test files."""

import hashlib
import math
import re
from collections.abc import Sequence

from ragbot.indexing.ports import Vector

DIMENSIONS = 64


class FakeEmbedder:
    """Deterministic "bag of words" embeddings: texts sharing words get similar vectors.

    Good enough to test ranking without calling Azure. Records every call.
    """

    def __init__(self) -> None:
        self.calls: list[list[str]] = []

    def embed(self, texts: Sequence[str]) -> list[Vector]:
        self.calls.append(list(texts))
        return [embed_words(t) for t in texts]


def embed_words(text: str) -> Vector:
    vector = [0.0] * DIMENSIONS
    for word in re.findall(r"\w+", text.lower()):
        slot = int(hashlib.sha256(word.encode()).hexdigest(), 16) % DIMENSIONS
        vector[slot] += 1.0
    norm = math.sqrt(sum(x * x for x in vector)) or 1.0
    return [x / norm for x in vector]
