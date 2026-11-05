"""Helpers shared by the chunkers. A leading underscore marks a module as internal."""

from ragbot.domain import Chunk, Document


def make_chunk(
    document: Document, position: int, heading: str | None, body: str, chunker: str
) -> Chunk:
    """Build a Chunk whose text starts with a context header (page title + heading path).

    A section such as "Opening hours" doesn't say *what* it is about; the header adds
    that context to the embedding and to the LLM prompt.
    """
    header = f"{document.title} > {heading}" if heading else document.title
    metadata = {"chunker": chunker}
    if document.section:
        metadata["section"] = document.section
    return Chunk(
        id=f"{document.url}#{position}",
        document_url=document.url,
        document_title=document.title,
        text=f"{header}\n\n{body}",
        position=position,
        heading=heading,
        metadata=metadata,
    )


def last_whitespace(text: str, start: int, end: int) -> int:
    """Index of the last whitespace character in text[start:end], or -1."""
    for i in range(end - 1, start - 1, -1):  # ≈ for (i = end - 1; i >= start; i--)
        if text[i].isspace():
            return i
    return -1
