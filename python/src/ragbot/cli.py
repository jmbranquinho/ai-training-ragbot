"""Command-line entry point (≈ Program.cs). Run with: uv run ragbot --help"""

import argparse


def main() -> None:
    parser = argparse.ArgumentParser(prog="ragbot", description="RAG chatbot (Python)")
    sub = parser.add_subparsers(dest="command", required=True)
    sub.add_parser("ingest", help="Scrape the website and build the index (offline pipeline)")
    sub.add_parser("chat", help="Ask questions (not implemented yet)")

    args = parser.parse_args()
    print(f"'{args.command}' is not implemented yet. Coming in later steps.")


if __name__ == "__main__":  # lets you also run: python -m ragbot.cli
    main()
