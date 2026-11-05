"""Command-line entry point (≈ Program.cs). Run with: uv run ragbot --help

This is the "composition root": the one place that picks concrete adapters and
wires them together. Everything else only knows the ports.
"""

import argparse
import logging
import math
from pathlib import Path

import httpx

from ragbot.indexing.chunkers import HeadingChunker, SlidingWindowChunker
from ragbot.indexing.deduplicate import deduplicate
from ragbot.indexing.ports import Chunker
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


def chunk(chunker: Chunker, data_dir: Path, show: str | None) -> None:
    """Chunk every stored page and print size statistics (nothing is saved yet)."""
    stored = list(FileDocumentStore(data_dir).all())
    documents = deduplicate(stored)
    print(f"{len(stored)} stored pages, {len(stored) - len(documents)} duplicates skipped")
    chunks_per_doc = [(doc, chunker.chunk(doc)) for doc in documents]
    sizes = sorted(len(c.text) for _, chunks in chunks_per_doc for c in chunks)

    if not sizes:
        print(f"No stored pages in {data_dir.resolve()}. Run `ingest` first.")
        return

    def pct(p: float) -> int:  # nearest-rank percentile (same definition as the .NET CLI)
        return sizes[max(0, math.ceil(p * len(sizes)) - 1)]

    print(f"{len(documents)} pages -> {len(sizes)} chunks ({len(sizes) / len(documents):.1f}/page)")
    print(f"chars: min {sizes[0]}, median {pct(0.5)}, p90 {pct(0.9)}, max {sizes[-1]}")
    print(f"chunks under 200 chars: {sum(s < 200 for s in sizes)}")

    for doc, chunks in chunks_per_doc:
        if show and show in doc.url:
            print(f"\n=== {doc.url} ({len(chunks)} chunks)")
            for c in chunks:
                print(f"\n--- #{c.position} [{len(c.text)} chars] heading={c.heading!r}\n{c.text}")
            break


def main() -> None:
    parser = argparse.ArgumentParser(prog="ragbot", description="RAG chatbot (Python)")
    sub = parser.add_subparsers(dest="command", required=True)

    ingest_cmd = sub.add_parser("ingest", help="Scrape the website and store raw pages")
    ingest_cmd.add_argument("--section", default="renovasjon-og-miljo", help="URL path section")
    ingest_cmd.add_argument("--max-pages", type=int, default=10)
    ingest_cmd.add_argument("--delay", type=float, default=1.0, help="seconds between requests")
    ingest_cmd.add_argument("--data-dir", type=Path, default=Path("data/raw"))

    chunk_cmd = sub.add_parser("chunk", help="Chunk stored pages and print statistics")
    chunk_cmd.add_argument("--strategy", choices=["headings", "window"], default="headings")
    chunk_cmd.add_argument("--max-chars", type=int, default=1500, help="headings: max body size")
    chunk_cmd.add_argument("--size", type=int, default=1000, help="window: window size")
    chunk_cmd.add_argument("--overlap", type=int, default=200, help="window: overlap")
    chunk_cmd.add_argument("--data-dir", type=Path, default=Path("data/raw"))
    chunk_cmd.add_argument(
        "--show", help="print the chunks of the first page whose URL contains this"
    )

    sub.add_parser("chat", help="Ask questions (not implemented yet)")

    args = parser.parse_args()
    logging.basicConfig(level=logging.INFO, format="%(message)s")
    logging.getLogger("httpx").setLevel(logging.WARNING)  # hide one log line per request

    if args.command == "ingest":
        ingest(args.section, args.max_pages, args.delay, args.data_dir)
    elif args.command == "chunk":
        chunker: Chunker = (
            HeadingChunker(args.max_chars)
            if args.strategy == "headings"
            else SlidingWindowChunker(args.size, args.overlap)
        )
        chunk(chunker, args.data_dir, args.show)
    else:
        print(f"'{args.command}' is not implemented yet. Coming in later steps.")


if __name__ == "__main__":  # lets you also run: python -m ragbot.cli
    main()
