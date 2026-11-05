from datetime import UTC, date, datetime

from ragbot.ingestion.adapters.stavanger_html import extract_links, parse_page

BASE = "https://www.stavanger.kommune.no"
URL = f"{BASE}/renovasjon-og-miljo/farlig-avfall/"
NOW = datetime(2026, 11, 5, tzinfo=UTC)


def test_parse_page_extracts_metadata(page_html: str) -> None:
    doc = parse_page(URL, page_html, fetched_at=NOW)

    assert doc.url == URL
    assert doc.title == "Batterier, maling og annet farlig avfall"
    assert doc.section == "renovasjon-og-miljo"
    assert doc.updated_on == date(2025, 10, 22)
    assert doc.fetched_at == NOW


def test_parse_page_converts_content_to_markdown(page_html: str) -> None:
    content = parse_page(URL, page_html, fetched_at=NOW).content

    assert content.startswith("# Batterier, maling og annet farlig avfall")
    assert "## Hva er farlig avfall?" in content
    assert "### Trygg levering" in content
    assert "- batterier" in content
    assert "Legg farlig avfall i den røde beholderen." in content  # HTML entities decoded
    assert "[gjenvinningsstasjonen](https://example.org/gjenvinning)" in content


def test_parse_page_strips_noise(page_html: str) -> None:
    content = parse_page(URL, page_html, fetched_at=NOW).content

    for noise in ["Del på Facebook", "Oppdatert", "Var siden nyttig", "should be ignored",
                  "Main menu", "Stavanger kommune", "piktogram", "\xa0"]:  # fmt: skip
        assert noise not in content, noise


def test_content_hash_changes_only_when_content_changes(page_html: str) -> None:
    first = parse_page(URL, page_html, fetched_at=NOW)
    later = parse_page(URL, page_html, fetched_at=datetime(2027, 1, 1, tzinfo=UTC))
    edited = parse_page(URL, page_html.replace("batterier", "lysrør"), fetched_at=NOW)

    assert first.content_hash == later.content_hash
    assert first.content_hash != edited.content_hash


def test_extract_links_keeps_same_host_links_under_prefix(site_map_html: str) -> None:
    links = extract_links(site_map_html, f"{BASE}/nettstedskart/", "/renovasjon-og-miljo/")

    assert links == [
        f"{BASE}/renovasjon-og-miljo/",
        f"{BASE}/renovasjon-og-miljo/farlig-avfall/",  # fragment duplicate removed
        f"{BASE}/renovasjon-og-miljo/matavfall/",  # query string removed
        f"{BASE}/renovasjon-og-miljo/hageavfall/",
        f"{BASE}/renovasjon-og-miljo/finn/",
    ]
