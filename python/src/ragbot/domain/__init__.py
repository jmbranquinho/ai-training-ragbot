"""The domain: the "what". No knowledge of databases, HTTP or LLM vendors here."""

from ragbot.domain.models import Answer, Chunk, Document, ScoredChunk

__all__ = ["Answer", "Chunk", "Document", "ScoredChunk"]
