"""Command-line entry point (≈ Program.cs). Run with: uv run ragbot --help

This is the "composition root": the one place that picks concrete adapters and
wires them together. Everything else only knows the ports.
"""

import argparse
import logging

import httpx

from ragbot.ingestion.adapters.http_scraper import HttpScraper, SiteMapUrlSource
from ragbot.ingestion.adapters.in_memory_store import InMemoryDocumentStore
from ragbot.ingestion.adapters.robots import RobotsPolicy

BASE_URL = "https://www.stavanger.kommune.no"
USER_AGENT = "RagBot-training/0.1 (learning project)"


def ingest(section: str, max_pages: int, delay: float, show: bool) -> None:
    with httpx.Client(  # `with` ≈ `using`: closes the client at the end
        headers={"User-Agent": USER_AGENT}, timeout=30, follow_redirects=True
    ) as client:
        robots = RobotsPolicy.load(client, BASE_URL, USER_AGENT)
        source = SiteMapUrlSource(
            client, robots, f"{BASE_URL}/nettstedskart/", f"/{section}/", max_pages
        )
        scraper = HttpScraper(client, robots, delay_seconds=delay)
        store = InMemoryDocumentStore()  # Step 3 replaces this with a persistent store

        for doc in scraper.fetch(source.discover()):
            store.save(doc)
            print(f"- {doc.title}  ({len(doc.content)} chars, updated {doc.updated_on})")
            print(f"  {doc.url}")

    documents = list(store.all())
    print(f"\nFetched {len(documents)} documents.")
    if show and documents:
        print(f"\n--- {documents[0].url} ---\n{documents[0].content}")


def main() -> None:
    parser = argparse.ArgumentParser(prog="ragbot", description="RAG chatbot (Python)")
    sub = parser.add_subparsers(dest="command", required=True)

    ingest_cmd = sub.add_parser("ingest", help="Scrape the website (offline pipeline)")
    ingest_cmd.add_argument("--section", default="renovasjon-og-miljo", help="URL path section")
    ingest_cmd.add_argument("--max-pages", type=int, default=10)
    ingest_cmd.add_argument("--delay", type=float, default=1.0, help="seconds between requests")
    ingest_cmd.add_argument("--show", action="store_true", help="print the first page's markdown")

    sub.add_parser("chat", help="Ask questions (not implemented yet)")

    args = parser.parse_args()
    logging.basicConfig(level=logging.INFO, format="%(levelname)s %(name)s: %(message)s")
    logging.getLogger("httpx").setLevel(logging.WARNING)  # hide one log line per request

    if args.command == "ingest":
        ingest(args.section, args.max_pages, args.delay, args.show)
    else:
        print(f"'{args.command}' is not implemented yet. Coming in later steps.")


if __name__ == "__main__":  # lets you also run: python -m ragbot.cli
    main()
