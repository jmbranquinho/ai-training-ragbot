"""Settings from environment variables and the repo's .env file (≈ IConfiguration).

Only the composition root (cli.py) reads settings; everything else gets plain values
passed in, so it stays testable without any configuration.
"""

import os
from dataclasses import dataclass

from dotenv import find_dotenv, load_dotenv


@dataclass(frozen=True)
class Settings:
    azure_openai_endpoint: str
    azure_openai_api_key: str | None  # None → sign in with Entra ID (az login)
    embedding_deployment: str
    embedding_dimensions: int
    postgres_conninfo: str

    @classmethod
    def load(cls) -> "Settings":
        load_dotenv(find_dotenv(usecwd=True))  # searches the current folder and its parents

        def env(name: str, default: str = "") -> str:
            return os.environ.get(name, default).strip()

        conninfo = (
            f"host={env('POSTGRES_HOST', 'localhost')} port={env('POSTGRES_PORT', '5433')} "
            f"dbname={env('POSTGRES_DB', 'ragbot')} user={env('POSTGRES_USER', 'ragbot')} "
            f"password={env('POSTGRES_PASSWORD', 'ragbot')}"
        )
        return cls(
            azure_openai_endpoint=env("AZURE_OPENAI_ENDPOINT"),
            azure_openai_api_key=env("AZURE_OPENAI_API_KEY") or None,
            embedding_deployment=env("AZURE_OPENAI_EMBEDDING_DEPLOYMENT", "text-embedding-3-small"),
            embedding_dimensions=int(env("EMBEDDING_DIMENSIONS", "1536")),
            postgres_conninfo=conninfo,
        )
