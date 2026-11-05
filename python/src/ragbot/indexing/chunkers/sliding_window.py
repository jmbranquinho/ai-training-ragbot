"""Chunk with fixed-size, overlapping windows over the whole page.

Ignores the page structure. Windows end at the last whitespace before `size`, and the
next window starts `overlap` characters earlier (at a word start), so a sentence cut at a
boundary still appears whole in one of the two neighbouring chunks.
"""

from ragbot.domain import Chunk, Document
from ragbot.indexing.chunkers._common import last_whitespace, make_chunk


class SlidingWindowChunker:
    name = "window"

    def __init__(self, size: int = 1000, overlap: int = 200) -> None:
        if size < 100:
            raise ValueError("size must be at least 100")
        if not 0 <= overlap < size // 2:
            raise ValueError("overlap must be between 0 and size/2")
        self._size = size
        self._overlap = overlap

    def chunk(self, document: Document) -> list[Chunk]:
        text = document.content
        chunks: list[Chunk] = []
        start = 0

        while start < len(text):
            end = min(start + self._size, len(text))
            if end < len(text):  # not the last window: end at a word boundary
                cut = last_whitespace(text, start + self._size // 2, end)
                if cut > 0:
                    end = cut

            piece = text[start:end].strip()
            if piece:
                chunks.append(make_chunk(document, len(chunks), None, piece, self.name))
            if end >= len(text):
                break

            start = end - self._overlap
            while start < end and not text[start - 1].isspace():  # move to a word start
                start += 1

        return chunks
