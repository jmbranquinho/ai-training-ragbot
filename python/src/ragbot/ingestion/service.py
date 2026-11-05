"""The ingest use case: discover → fetch → store, writing only what changed.

This is application logic, not an adapter: it only talks to ports, so the same code
works with the real website + files, or with fakes in tests.
"""

import logging
from dataclasses import dataclass, field

from ragbot.ingestion.ports import DocumentStore, Scraper, UrlSource

logger = logging.getLogger(__name__)


@dataclass  # not frozen: the service fills it in as it goes
class IngestReport:
    new: list[str] = field(default_factory=list)
    updated: list[str] = field(default_factory=list)
    unchanged: list[str] = field(default_factory=list)


class IngestService:
    def __init__(self, source: UrlSource, scraper: Scraper, store: DocumentStore) -> None:
        self._source = source
        self._scraper = scraper
        self._store = store

    def run(self) -> IngestReport:
        report = IngestReport()

        for document in self._scraper.fetch(self._source.discover()):
            existing = self._store.get(document.url)

            if existing is None:
                report.new.append(document.url)
                status = "new"
            elif existing.content_hash != document.content_hash:
                report.updated.append(document.url)
                status = "updated"
            else:
                report.unchanged.append(document.url)
                logger.info("unchanged  %s", document.url)
                continue  # keep the existing file (and its original fetched_at)

            self._store.save(document)
            logger.info("%-9s  %s", status, document.url)

        return report
