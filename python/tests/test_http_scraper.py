"""Scraper tests that never touch the network: httpx.MockTransport plays the website."""

import httpx

from ragbot.ingestion.adapters.http_scraper import HttpScraper, SiteMapUrlSource
from ragbot.ingestion.adapters.robots import RobotsPolicy

BASE = "https://www.stavanger.kommune.no"
UA = "RagBot-test"


def make_client(routes: dict[str, httpx.Response]) -> httpx.Client:
    def handler(request: httpx.Request) -> httpx.Response:
        return routes.get(str(request.url), httpx.Response(404))

    return httpx.Client(transport=httpx.MockTransport(handler))


def html(body: str) -> httpx.Response:
    return httpx.Response(200, text=body, headers={"content-type": "text/html; charset=utf-8"})


def test_robots_policy(robots_txt: str) -> None:
    robots = RobotsPolicy(robots_txt, UA)

    assert robots.is_allowed(f"{BASE}/renovasjon-og-miljo/")
    assert robots.is_allowed(f"{BASE}/renovasjon-og-miljo/finn/")  # rule is a path *prefix*
    assert not robots.is_allowed(f"{BASE}/finn/noe/")
    assert not robots.is_allowed(f"{BASE}/api/x")


def test_missing_robots_txt_allows_everything() -> None:
    robots = RobotsPolicy.load(make_client({}), BASE, UA)

    assert robots.is_allowed(f"{BASE}/finn/")


def test_site_map_source_respects_robots_and_max_pages() -> None:
    site_map = """<a href="/renovasjon-og-miljo/a/">a</a>
                   <a href="/renovasjon-og-miljo/b/">b</a>
                   <a href="/renovasjon-og-miljo/c/">c</a>
                   <a href="/renovasjon-og-miljo/d/">d</a>"""
    client = make_client({f"{BASE}/nettstedskart/": html(site_map)})
    robots = RobotsPolicy("User-agent: *\nDisallow: /renovasjon-og-miljo/b/", UA)

    source = SiteMapUrlSource(client, robots, f"{BASE}/nettstedskart/", "/renovasjon-og-miljo/", 2)

    # b is disallowed, so the first 2 allowed pages are a and c
    assert source.discover() == [f"{BASE}/renovasjon-og-miljo/a/", f"{BASE}/renovasjon-og-miljo/c/"]


def test_scraper_returns_documents_and_skips_failures(page_html: str, robots_txt: str) -> None:
    good = f"{BASE}/renovasjon-og-miljo/farlig-avfall/"
    missing = f"{BASE}/renovasjon-og-miljo/borte/"
    pdf = f"{BASE}/renovasjon-og-miljo/fil.pdf"
    blocked = f"{BASE}/finn/noe/"
    client = make_client(
        {
            good: html(page_html),
            pdf: httpx.Response(200, content=b"%PDF", headers={"content-type": "application/pdf"}),
        }
    )
    sleeps: list[float] = []
    scraper = HttpScraper(client, RobotsPolicy(robots_txt, UA), delay_seconds=0.5,
                          sleep=sleeps.append)  # fmt: skip

    docs = list(scraper.fetch([good, missing, pdf, blocked]))

    assert [d.url for d in docs] == [good]
    assert docs[0].title == "Batterier, maling og annet farlig avfall"
    assert sleeps == [0.5, 0.5]  # waited between requests, but not before the first one
