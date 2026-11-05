"""Command-line entry point (≈ Program.cs). Run with: uv run ragbot --help

This is the "composition root": the one place that picks concrete adapters and
wires them together. Everything else only knows the ports.
"""

import argparse
import json
import logging
import math
from dataclasses import asdict
from datetime import datetime
from pathlib import Path

import httpx

from ragbot.azure_openai import create_client
from ragbot.chat.adapters.azure_answer_generator import AzureOpenAIAnswerGenerator
from ragbot.chat.ports import Retriever
from ragbot.chat.retrieval import HybridRetriever, KeywordRetriever, VectorRetriever
from ragbot.chat.service import ChatService
from ragbot.config import Settings
from ragbot.domain import Answer
from ragbot.evaluation.azure_judge import AzureOpenAIJudge
from ragbot.evaluation.dataset import load_questions
from ragbot.evaluation.service import KS, AnswerResult, evaluate_answers, evaluate_retrieval
from ragbot.indexing.adapters.azure_embedder import AzureOpenAIEmbedder
from ragbot.indexing.adapters.pgvector_index import PgVectorChunkIndex
from ragbot.indexing.chunkers import HeadingChunker, SlidingWindowChunker
from ragbot.indexing.deduplicate import deduplicate
from ragbot.indexing.ports import Chunker
from ragbot.indexing.service import IndexService
from ragbot.ingestion.adapters.file_store import FileDocumentStore
from ragbot.ingestion.adapters.http_scraper import HttpScraper, SiteMapUrlSource
from ragbot.ingestion.adapters.robots import RobotsPolicy
from ragbot.ingestion.service import IngestService

BASE_URL = "https://www.stavanger.kommune.no"
USER_AGENT = "RagBot-training/0.1"
DB_SCHEMA = "python"  # the .NET version uses "dotnet" in the same database
MODES = ["vector", "keyword", "hybrid"]
QUESTIONS = Path(__file__).resolve().parents[3] / "eval" / "questions.json"


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
    """Chunk every stored page and print size statistics (nothing is saved)."""
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


def index(chunker: Chunker, data_dir: Path, rebuild: bool, schema: str) -> None:
    settings = Settings.load()
    service = IndexService(
        store=FileDocumentStore(data_dir),
        chunker=chunker,
        embedder=make_embedder(settings),
        index=PgVectorChunkIndex(settings.postgres_conninfo, schema, settings.embedding_dimensions),
    )

    report = service.run(rebuild=rebuild)

    print(
        f"\n{len(report.indexed)} pages indexed ({report.chunks_embedded} chunks embedded), "
        f"{len(report.unchanged)} unchanged, {len(report.deleted)} removed."
    )


def search(question: str, top_k: int, mode: str, schema: str) -> None:
    settings = Settings.load()
    retriever = make_retriever(settings, mode, schema)

    for i, hit in enumerate(retriever.retrieve(question, top_k), start=1):
        first_line = hit.chunk.text.splitlines()[0]
        print(f"{i}. [{hit.score:.3f}] {first_line}\n   {hit.chunk.document_url}")


def eval_retrieval(questions_path: Path, modes: list[str], schema: str) -> None:
    settings = Settings.load()
    questions = load_questions(questions_path)
    print(f"Retrieval on {schema}: {sum(q.answerable for q in questions)} answerable questions")
    print(f"{'mode':8} {'hit@1':>6} {'hit@3':>6} {'hit@5':>6} {'hit@10':>6} {'MRR':>6}  not found")
    for mode in modes:
        results, s = evaluate_retrieval(make_retriever(settings, mode, schema), questions)
        hits = " ".join(f"{s.hit_at[k]:>6.0%}" for k in KS)
        missed = [r.question_id for r in results if r.rank is None]
        print(f"{mode:8} {hits} {s.mrr:>6.3f}  {s.not_found} {missed if missed else ''}")
        save_results(f"retrieval-{schema}-{mode}", [asdict(r) for r in results])


def eval_answers(questions_path: Path, mode: str, top_k: int, schema: str) -> None:
    settings = Settings.load()
    client = create_client(settings.azure_openai_endpoint, settings.azure_openai_api_key)
    judge = AzureOpenAIJudge(client, settings.chat_deployment)
    questions = load_questions(questions_path)

    results = evaluate_answers(make_chat_service(settings, mode, top_k, schema), judge, questions)

    for r in results:
        print(
            f"{r.question_id} {r.grade:9} cited={'y' if r.cited_expected_page else '-'}  {r.reason}"
        )
    answerable = [r for r in results if r.answerable]
    unanswerable = [r for r in results if not r.answerable]

    def share(rows: list[AnswerResult], grade: str) -> str:
        return f"{sum(r.grade == grade for r in rows)}/{len(rows)}"

    print(f"\n{schema} / {mode} / top_k={top_k}")
    cited = sum(r.cited_expected_page for r in answerable)
    refused = sum(r.said_unknown for r in answerable)
    print(
        f"  answerable:   correct {share(answerable, 'correct')}, "
        f"partial {share(answerable, 'partial')}, incorrect {share(answerable, 'incorrect')}, "
        f"cited a correct page {cited}/{len(answerable)}, wrongly said 'don't know' {refused}"
    )
    print(f"  unanswerable: correctly refused {share(unanswerable, 'correct')}")
    save_results(f"answers-{schema}-{mode}-k{top_k}", [asdict(r) for r in results])


def save_results(name: str, rows: list[dict[str, object]]) -> None:
    """Full per-question results, so runs can be compared later (eval/results/, git-ignored)."""
    folder = Path(__file__).resolve().parents[3] / "eval" / "results"
    folder.mkdir(parents=True, exist_ok=True)
    stamp = datetime.now().strftime("%Y%m%d-%H%M%S")
    path = folder / f"{stamp}-python-{name}.json"
    path.write_text(json.dumps(rows, ensure_ascii=False, indent=2), encoding="utf-8")


def make_retriever(settings: Settings, mode: str, schema: str) -> Retriever:
    chunk_index = PgVectorChunkIndex(
        settings.postgres_conninfo, schema, settings.embedding_dimensions
    )
    if mode == "keyword":
        return KeywordRetriever(chunk_index)  # no embedding call needed
    if mode == "vector":
        return VectorRetriever(make_embedder(settings), chunk_index)
    return HybridRetriever(make_embedder(settings), chunk_index)


def make_chat_service(settings: Settings, mode: str, top_k: int, schema: str) -> ChatService:
    client = create_client(settings.azure_openai_endpoint, settings.azure_openai_api_key)
    generator = AzureOpenAIAnswerGenerator(client, settings.chat_deployment)
    return ChatService(make_retriever(settings, mode, schema), generator, top_k)


def print_answer(answer: Answer) -> None:
    print(f"\n{answer.text}")
    if answer.sources:
        print("\nKilder:")  # "Sources"
        for i, source in enumerate(answer.sources, start=1):
            where = f" > {source.heading}" if source.heading else ""
            print(f"  {i}. {source.document_title}{where}\n     {source.document_url}")


def ask(question: str, mode: str, top_k: int, schema: str) -> None:
    print_answer(make_chat_service(Settings.load(), mode, top_k, schema).ask(question))


def chat(mode: str, top_k: int, schema: str) -> None:
    service = make_chat_service(Settings.load(), mode, top_k, schema)
    print("Spør om renovasjon og miljø i Stavanger kommune. Tom linje avslutter.")
    while question := input("\n> ").strip():  # loop until an empty line
        print_answer(service.ask(question))


def make_embedder(settings: Settings) -> AzureOpenAIEmbedder:
    client = create_client(settings.azure_openai_endpoint, settings.azure_openai_api_key)
    return AzureOpenAIEmbedder(client, settings.embedding_deployment, settings.embedding_dimensions)


def add_chunker_args(parser: argparse.ArgumentParser) -> None:
    parser.add_argument("--strategy", choices=["headings", "window"], default="headings")
    parser.add_argument("--max-chars", type=int, default=1500, help="headings: max body size")
    parser.add_argument("--size", type=int, default=1000, help="window: window size")
    parser.add_argument("--overlap", type=int, default=200, help="window: overlap")
    parser.add_argument("--data-dir", type=Path, default=Path("data/raw"))


def make_chunker(args: argparse.Namespace) -> Chunker:
    if args.strategy == "headings":
        return HeadingChunker(args.max_chars)
    return SlidingWindowChunker(args.size, args.overlap)


def main() -> None:
    parser = argparse.ArgumentParser(prog="ragbot", description="RAG chatbot (Python)")
    sub = parser.add_subparsers(dest="command", required=True)

    ingest_cmd = sub.add_parser("ingest", help="Scrape the website and store raw pages")
    ingest_cmd.add_argument("--section", default="renovasjon-og-miljo", help="URL path section")
    ingest_cmd.add_argument("--max-pages", type=int, default=10)
    ingest_cmd.add_argument("--delay", type=float, default=1.0, help="seconds between requests")
    ingest_cmd.add_argument("--data-dir", type=Path, default=Path("data/raw"))

    chunk_cmd = sub.add_parser("chunk", help="Chunk stored pages and print statistics")
    add_chunker_args(chunk_cmd)
    chunk_cmd.add_argument(
        "--show", help="print the chunks of the first page whose URL contains this"
    )

    index_cmd = sub.add_parser("index", help="Chunk, embed and store pages in pgvector")
    add_chunker_args(index_cmd)
    index_cmd.add_argument("--rebuild", action="store_true", help="drop the index first")

    search_cmd = sub.add_parser("search", help="Show the chunks most relevant to a question")
    search_cmd.add_argument("question")
    search_cmd.add_argument("--top-k", type=int, default=5)
    search_cmd.add_argument("--mode", choices=MODES, default="hybrid")

    ask_cmd = sub.add_parser("ask", help="Answer one question with sources")
    ask_cmd.add_argument("question")
    chat_cmd = sub.add_parser("chat", help="Interactive chat (empty line quits)")
    for cmd in (ask_cmd, chat_cmd):
        cmd.add_argument("--top-k", type=int, default=5, help="chunks given to the model")
        cmd.add_argument("--mode", choices=MODES, default="hybrid")

    eval_cmd = sub.add_parser("eval", help="Evaluate on eval/questions.json")
    eval_cmd.add_argument("what", choices=["retrieval", "answers"])
    eval_cmd.add_argument("--modes", default="vector,keyword,hybrid", help="retrieval: modes")
    eval_cmd.add_argument("--mode", choices=MODES, default="hybrid", help="answers: mode")
    eval_cmd.add_argument("--top-k", type=int, default=5, help="answers: chunks for the model")
    eval_cmd.add_argument("--questions", type=Path, default=QUESTIONS)

    for cmd in (index_cmd, search_cmd, ask_cmd, chat_cmd, eval_cmd):
        cmd.add_argument("--schema", default=DB_SCHEMA, help="database schema (index variant)")

    args = parser.parse_args()
    logging.basicConfig(level=logging.INFO, format="%(message)s")
    # hide one log line per HTTP request (openai 3.x uses httpx2, our scraper uses httpx)
    for noisy in ("httpx", "httpx2", "azure", "openai"):
        logging.getLogger(noisy).setLevel(logging.WARNING)

    if args.command == "ingest":
        ingest(args.section, args.max_pages, args.delay, args.data_dir)
    elif args.command == "chunk":
        chunk(make_chunker(args), args.data_dir, args.show)
    elif args.command == "index":
        index(make_chunker(args), args.data_dir, args.rebuild, args.schema)
    elif args.command == "search":
        search(args.question, args.top_k, args.mode, args.schema)
    elif args.command == "ask":
        ask(args.question, args.mode, args.top_k, args.schema)
    elif args.command == "chat":
        chat(args.mode, args.top_k, args.schema)
    elif args.command == "eval" and args.what == "retrieval":
        eval_retrieval(args.questions, args.modes.split(","), args.schema)
    elif args.command == "eval":
        eval_answers(args.questions, args.mode, args.top_k, args.schema)


if __name__ == "__main__":  # lets you also run: python -m ragbot.cli
    main()
