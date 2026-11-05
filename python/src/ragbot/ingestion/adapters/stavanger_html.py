"""HTML → Document conversion for stavanger.kommune.no.

These are pure functions (no HTTP, no clock), which makes them easy to test with
saved HTML files. Everything specific to this website's markup lives here, so
switching to another site means writing a new module like this one.
"""

import re
from datetime import date, datetime
from urllib.parse import urldefrag, urljoin, urlparse

from bs4 import BeautifulSoup, Tag
from markdownify import markdownify

from ragbot.domain import Document

# Elements inside <main> that are page chrome, not content.
_NOISE_SELECTORS = [
    "script",
    "style",
    "form",
    "aside.byline",  # "updated" date + share buttons
    "ul.anchorlinks",  # in-page table of contents
    ".image-wrapper",
    ".formarea",  # "was this page helpful?"
    "img",
]
# Teaser cards wrap a heading + paragraph in one big <a>. Converted as-is, each card becomes
# a single markdown link with the heading inside it, which is poor input for embeddings.
_CARD_LINK_SELECTOR = "div.teaser > a"
_DATE_PATTERN = re.compile(r"(\d{2})\.(\d{2})\.(\d{4})")  # 22.10.2025


def parse_page(url: str, html: str, fetched_at: datetime) -> Document:
    soup = BeautifulSoup(html, "html.parser")
    main = soup.find("main") or soup.body or soup

    updated_on = _parse_updated_on(soup)
    title = _parse_title(soup)

    for selector in _NOISE_SELECTORS:
        for element in main.select(selector):
            element.decompose()  # remove from the tree

    for card_link in main.select(_CARD_LINK_SELECTOR):
        card_link.unwrap()  # replace the <a> with its children: keep heading + text, drop link

    return Document(
        url=url,
        title=title,
        content=_to_markdown(str(main)),
        fetched_at=fetched_at,
        section=_section_from_url(url),
        updated_on=updated_on,
    )


def extract_links(html: str, base_url: str, path_prefix: str) -> list[str]:
    """All unique same-host links whose path starts with `path_prefix`, in page order."""
    soup = BeautifulSoup(html, "html.parser")
    base_host = urlparse(base_url).netloc
    seen: dict[str, None] = {}  # dict keeps insertion order, used as an ordered set

    for anchor in soup.find_all("a", href=True):
        if not isinstance(anchor, Tag):
            continue
        absolute, _fragment = urldefrag(urljoin(base_url, str(anchor["href"])))
        parsed = urlparse(absolute)
        if parsed.scheme not in ("http", "https") or parsed.netloc != base_host:
            continue
        if not parsed.path.startswith(path_prefix):
            continue
        clean = parsed._replace(query="").geturl()  # drop ?ref=menu etc.
        seen.setdefault(clean, None)

    return list(seen)


def _parse_title(soup: BeautifulSoup) -> str:
    if (h1 := soup.select_one("h1#pagetitle")) is not None:  # `:=` assigns inside an expression
        return h1.get_text(strip=True)
    if soup.title is not None:
        return soup.title.get_text(strip=True).removesuffix(" | Stavanger kommune")
    return ""


def _parse_updated_on(soup: BeautifulSoup) -> date | None:
    element = soup.select_one(".byline-updated")
    match = _DATE_PATTERN.search(element.get_text()) if element else None
    if match is None:
        return None
    day, month, year = (int(part) for part in match.groups())
    return date(year, month, day)


def _section_from_url(url: str) -> str | None:
    """First path segment, e.g. /renovasjon-og-miljo/farlig-avfall/ → renovasjon-og-miljo."""
    segments = [s for s in urlparse(url).path.split("/") if s]
    return segments[0] if segments else None


def _to_markdown(html: str) -> str:
    markdown = markdownify(html, heading_style="ATX", bullets="-")
    markdown = markdown.replace("\xa0", " ")  # &nbsp;
    markdown = re.sub(r"[ \t]+\n", "\n", markdown)  # trailing spaces
    markdown = re.sub(r"\n{3,}", "\n\n", markdown)  # collapse blank lines
    return markdown.strip()
