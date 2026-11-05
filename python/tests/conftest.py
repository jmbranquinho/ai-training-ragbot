"""Shared pytest fixtures. pytest loads conftest.py automatically.

A pytest "fixture" is a function whose return value is injected into any test that
has a parameter with the same name: dependency injection for tests.
"""

from pathlib import Path

import pytest

TESTDATA = Path(__file__).resolve().parents[2] / "testdata" / "stavanger"


def read_testdata(name: str) -> str:
    return (TESTDATA / name).read_text(encoding="utf-8")


@pytest.fixture
def page_html() -> str:
    return read_testdata("page.html")


@pytest.fixture
def landing_html() -> str:
    return read_testdata("landing.html")


@pytest.fixture
def site_map_html() -> str:
    return read_testdata("nettstedskart.html")


@pytest.fixture
def robots_txt() -> str:
    return read_testdata("robots.txt")
