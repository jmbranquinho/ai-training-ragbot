"""robots.txt policy, using Python's built-in parser (urllib.robotparser)."""

from urllib.robotparser import RobotFileParser

import httpx


class RobotsPolicy:
    def __init__(self, robots_txt: str, user_agent: str) -> None:
        self._user_agent = user_agent
        self._parser = RobotFileParser()
        self._parser.parse(robots_txt.splitlines())

    @classmethod  # ≈ a static factory method
    def load(cls, client: httpx.Client, base_url: str, user_agent: str) -> "RobotsPolicy":
        """Download robots.txt. A missing file (404) means everything is allowed."""
        response = client.get(f"{base_url.rstrip('/')}/robots.txt")
        text = response.text if response.status_code == httpx.codes.OK else ""
        return cls(text, user_agent)

    def is_allowed(self, url: str) -> bool:
        return self._parser.can_fetch(self._user_agent, url)
