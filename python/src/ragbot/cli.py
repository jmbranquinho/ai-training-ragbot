"""Command-line entry point (≈ Program.cs). Run with: uv run ragbot --help

This is the "composition root": the one place that picks concrete adapters and
wires them together. Everything else only knows the ports.
"""

import argparse
import logging
from pathlib import Path

import httpx

from ragbot.ingestion.adapters.file_store import FileDocumentStore
from ragbot.ingestion.adapters.http_scraper import HttpScraper, SiteMapUrlSource
from ragbot.ingestion.adapters.robots import RobotsPolicy
from ragbot.ingestion.service import IngestService

BASE_URL = "https://www.stavanger.kommune.no"
USER_AGENT = "RagBot-training/0.1"


def ingest(section: str, max_pages: int, delay: float, data_dir: Path) -> None:
    with httpx.Client(  # `with` ≈ `using`: closes the client at the end
        headers={"User-Agent": USER_AGENT}, timeout=30, follow_redirects=True
    ) as client:
        robots = RobotsPolicy.load(client, BASE_URL, USER_AGENT)
        source = SiteMapUrlSource(
            client, robots, f"{BASE_URL}/nettstedskart/", f"/{section}/", max_pages
        )
        scraper = HttpScraper(client, robots, delay_seconds=delay)
        store = FileDocumentStore(data_dir)

        report = IngestService(source, scraper, store).run()

    print(
        f"\n{len(report.new)} new, {len(report.updated)} updated, "
        f"{len(report.unchanged)} unchanged. Files in {data_dir.resolve()}"
    )


def main() -> None:
    parser = argparse.ArgumentParser(prog="ragbot", description="RAG chatbot (Python)")
    sub = parser.add_subparsers(dest="command", required=True)

    ingest_cmd = sub.add_parser("ingest", help="Scrape the website and store raw pages")
    ingest_cmd.add_argument("--section", default="renovasjon-og-miljo", help="URL path section")
    ingest_cmd.add_argument("--max-pages", type=int, default=10)
    ingest_cmd.add_argument("--delay", type=float, default=1.0, help="seconds between requests")
    ingest_cmd.add_argument("--data-dir", type=Path, default=Path("data/raw"))

    sub.add_parser("chat", help="Ask questions (not implemented yet)")

    args = parser.parse_args()
    logging.basicConfig(level=logging.INFO, format="%(message)s")
    logging.getLogger("httpx").setLevel(logging.WARNING)  # hide one log line per request

    if args.command == "ingest":
        ingest(args.section, args.max_pages, args.delay, args.data_dir)
    else:
        print(f"'{args.command}' is not implemented yet. Coming in later steps.")


if __name__ == "__main__":  # lets you also run: python -m ragbot.cli
    main()
