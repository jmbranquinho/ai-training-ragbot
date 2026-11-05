"""DocumentStore that keeps each page as a markdown file with a small metadata header.

Layout:  <root>/<host>/<url path>.md
         e.g. data/raw/www.stavanger.kommune.no/renovasjon-og-miljo/farlig-avfall.md

Format (identical in the .NET version, pinned by testdata/stavanger/stored-document.md):

    ---
    url: https://...
    title: ...
    section: ...          (optional)
    updated_on: 2025-10-22 (optional)
    fetched_at: 2026-11-05T09:30:00+00:00
    content_hash: <sha256>
    ---

    <markdown content>
"""

import os
import re
from collections.abc import Iterator
from datetime import UTC, date, datetime
from pathlib import Path  # ≈ System.IO.Path + FileInfo in one object
from urllib.parse import unquote, urlparse

from ragbot.domain import Document

_FENCE = "---"
_UNSAFE_CHARS = re.compile(r"[^A-Za-z0-9._-]")


class FileDocumentStore:
    def __init__(self, root: Path) -> None:
        self._root = root

    def save(self, document: Document) -> None:
        path = self.path_for(document.url)
        path.parent.mkdir(parents=True, exist_ok=True)
        # Write to a temp file, then swap it in, so a crash never leaves a half-written file.
        temp = path.with_suffix(".md.tmp")
        temp.write_bytes(serialize(document).encode("utf-8"))  # bytes: no CRLF on Windows
        os.replace(temp, path)  # ≈ File.Move(temp, path, overwrite: true)

    def get(self, url: str) -> Document | None:
        path = self.path_for(url)
        return deserialize(path.read_text(encoding="utf-8")) if path.is_file() else None

    def all(self) -> Iterator[Document]:
        for path in sorted(self._root.rglob("*.md")):  # rglob = recursive glob
            yield deserialize(path.read_text(encoding="utf-8"))

    def path_for(self, url: str) -> Path:
        parsed = urlparse(url)
        # unquote: %C3%B8 → ø, so encoded and plain URLs map to the same file
        segments = [_safe(unquote(s)) for s in parsed.path.split("/") if s] or ["index"]
        return self._root.joinpath(_safe(parsed.netloc), *segments[:-1], f"{segments[-1]}.md")


def serialize(document: Document) -> str:
    fields = {
        "url": document.url,
        "title": document.title,
        "section": document.section,
        "updated_on": document.updated_on.isoformat() if document.updated_on else None,
        "fetched_at": document.fetched_at.astimezone(UTC).isoformat(timespec="seconds"),
        "content_hash": document.content_hash,
    }
    lines = [_FENCE]
    for key, value in fields.items():  # .items() ≈ iterating KeyValuePairs
        if value is None:
            continue
        if "\n" in value:
            raise ValueError(f"{key} cannot contain a line break: {value!r}")
        lines.append(f"{key}: {value}")
    lines.append(_FENCE)
    return "\n".join(lines) + "\n\n" + document.content + "\n"


def deserialize(text: str) -> Document:
    text = text.replace("\r\n", "\n")
    if not text.startswith(_FENCE + "\n"):
        raise ValueError("missing metadata header")
    header, separator, body = text.removeprefix(_FENCE + "\n").partition("\n" + _FENCE + "\n")
    if not separator:
        raise ValueError("metadata header is not closed")

    fields: dict[str, str] = {}
    for line in header.splitlines():
        key, _, value = line.partition(":")  # split on the FIRST colon only
        fields[key.strip()] = value.strip()

    return Document(
        url=fields["url"],
        title=fields.get("title", ""),
        content=body.strip("\n"),
        fetched_at=datetime.fromisoformat(fields["fetched_at"]),
        section=fields.get("section"),
        updated_on=date.fromisoformat(fields["updated_on"]) if "updated_on" in fields else None,
    )


def _safe(segment: str) -> str:
    return _UNSAFE_CHARS.sub("_", segment)
