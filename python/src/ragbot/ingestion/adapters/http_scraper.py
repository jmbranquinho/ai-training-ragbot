"""Scraper and UrlSource adapters that talk to the real website over HTTP."""

import logging
import time
from collections.abc import Callable, Iterable, Iterator
from datetime import UTC, datetime

import httpx

from ragbot.domain import Document
from ragbot.ingestion.adapters import stavanger_html
from ragbot.ingestion.adapters.robots import RobotsPolicy

logger = logging.getLogger(__name__)  # ≈ ILogger<HttpScraper>


class SiteMapUrlSource:
    """Discovers pages from the site's HTML site map (/nettstedskart/)."""

    def __init__(
        self,
        client: httpx.Client,
        robots: RobotsPolicy,
        site_map_url: str,
        path_prefix: str,
        max_pages: int,
    ) -> None:
        self._client = client
        self._robots = robots
        self._site_map_url = site_map_url
        self._path_prefix = path_prefix
        self._max_pages = max_pages

    def discover(self) -> list[str]:
        response = self._client.get(self._site_map_url)
        response.raise_for_status()  # ≈ EnsureSuccessStatusCode()
        links = stavanger_html.extract_links(response.text, self._site_map_url, self._path_prefix)
        allowed = [url for url in links if self._robots.is_allowed(url)]
        logger.info("Discovered %d pages (%d allowed by robots.txt)", len(links), len(allowed))
        return allowed[: self._max_pages]  # slice: first max_pages items (≈ .Take(n))


class HttpScraper:
    """Fetches pages politely (robots.txt + delay between requests) and parses them."""

    def __init__(
        self,
        client: httpx.Client,
        robots: RobotsPolicy,
        delay_seconds: float = 1.0,
        sleep: Callable[[float], None] = time.sleep,  # injectable so tests don't wait
    ) -> None:
        self._client = client
        self._robots = robots
        self._delay_seconds = delay_seconds
        self._sleep = sleep

    def fetch(self, urls: Iterable[str]) -> Iterator[Document]:
        for i, url in enumerate(urls):  # enumerate ≈ .Select((url, i) => ...)
            if not self._robots.is_allowed(url):
                logger.info("Skipping %s (disallowed by robots.txt)", url)
                continue
            if i > 0:
                self._sleep(self._delay_seconds)

            try:
                response = self._client.get(url)
            except httpx.HTTPError as error:
                logger.warning("Failed to fetch %s: %s", url, error)
                continue

            if response.status_code != httpx.codes.OK:
                logger.warning("Skipping %s (HTTP %d)", url, response.status_code)
                continue
            if "text/html" not in response.headers.get("content-type", ""):
                logger.info("Skipping %s (not HTML)", url)
                continue

            yield stavanger_html.parse_page(url, response.text, fetched_at=datetime.now(UTC))
