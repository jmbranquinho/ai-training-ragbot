"""Drop pages whose content is identical to another page's.

stavanger.kommune.no serves some long plans in full on every chapter URL (one plan appears
under 81 URLs). Indexing all copies would fill every search result with the same text, so
we keep one page per content hash. The raw store keeps all copies: it records what the site
serves, and this decision belongs to indexing.
"""

from collections.abc import Iterable

from ragbot.domain import Document


def deduplicate(documents: Iterable[Document]) -> list[Document]:
    """One document per content hash: the one with the shortest URL (ties: alphabetical).

    For the plans, the shortest URL is the plan's root page, which is a good page to cite.
    Order of the result follows the first occurrence of each content.
    """
    best: dict[str, Document] = {}
    for doc in documents:
        current = best.get(doc.content_hash)
        if current is None or (len(doc.url), doc.url) < (len(current.url), current.url):
            best[doc.content_hash] = doc  # tuples compare element by element
    return list(best.values())
