"""ChunkIndex in PostgreSQL + pgvector. One `chunks` table per schema (python / dotnet).

Table layout (identical in the .NET version):
  id, document_url, document_version, document_title, heading, position, text, metadata,
  embedding vector(N)  -- searched with cosine distance through an HNSW index
  tsv tsvector         -- generated Norwegian full-text column, for hybrid search in Step 6
"""

import json
from collections.abc import Sequence
from typing import Any

import psycopg
from psycopg import sql  # safe quoting of identifiers like the schema name

from ragbot.domain import Chunk, ScoredChunk
from ragbot.indexing.ports import Vector

_CREATE = """
CREATE EXTENSION IF NOT EXISTS vector;
CREATE SCHEMA IF NOT EXISTS {schema};
CREATE TABLE IF NOT EXISTS {schema}.chunks (
    id               text PRIMARY KEY,
    document_url     text NOT NULL,
    document_version text NOT NULL,
    document_title   text NOT NULL,
    heading          text,
    position         integer NOT NULL,
    text             text NOT NULL,
    metadata         jsonb NOT NULL DEFAULT '{{}}'::jsonb,
    embedding        vector({dimensions}) NOT NULL,
    tsv              tsvector GENERATED ALWAYS AS (to_tsvector('norwegian', text)) STORED
);
CREATE INDEX IF NOT EXISTS chunks_document_url ON {schema}.chunks (document_url);
CREATE INDEX IF NOT EXISTS chunks_embedding ON {schema}.chunks
    USING hnsw (embedding vector_cosine_ops);
CREATE INDEX IF NOT EXISTS chunks_tsv ON {schema}.chunks USING gin (tsv);
"""


class PgVectorChunkIndex:
    def __init__(self, conninfo: str, schema: str, dimensions: int) -> None:
        self._conninfo = conninfo
        self._schema = sql.Identifier(schema)
        self._schema_name = schema
        self._dimensions = dimensions
        self._ready = False  # table is created/checked on first use, so clear() can run first

    def document_versions(self) -> dict[str, str]:
        self._ensure_table()
        query = sql.SQL("SELECT DISTINCT document_url, document_version FROM {}.chunks")
        with psycopg.connect(self._conninfo) as conn:
            rows = conn.execute(query.format(self._schema)).fetchall()
        return {url: version for url, version in rows}

    def replace_document(
        self, document_url: str, version: str, chunks: Sequence[Chunk], vectors: Sequence[Vector]
    ) -> None:
        if len(chunks) != len(vectors):
            raise ValueError("need exactly one vector per chunk")
        self._ensure_table()
        insert = sql.SQL(
            "INSERT INTO {}.chunks (id, document_url, document_version, document_title, heading,"
            " position, text, metadata, embedding)"
            " VALUES (%s, %s, %s, %s, %s, %s, %s, %s, %s::vector)"
        ).format(self._schema)
        rows = [
            (
                c.id,
                document_url,
                version,
                c.document_title,
                c.heading,
                c.position,
                c.text,
                json.dumps(c.metadata),
                v,
            )
            for c, v in zip(chunks, vectors, strict=True)
        ]
        # `with conn:` commits on success and rolls back on error: delete + insert is atomic
        with psycopg.connect(self._conninfo) as conn, conn.cursor() as cur:
            cur.execute(self._delete_sql(), (document_url,))
            cur.executemany(insert, rows)

    def delete_document(self, document_url: str) -> None:
        self._ensure_table()
        with psycopg.connect(self._conninfo) as conn:
            conn.execute(self._delete_sql(), (document_url,))

    def clear(self) -> None:
        """Drop and recreate the table (also picks up a changed embedding size)."""
        with psycopg.connect(self._conninfo) as conn:
            conn.execute(sql.SQL("DROP TABLE IF EXISTS {}.chunks").format(self._schema))
        self._ready = False
        self._ensure_table()

    def search(self, query_vector: Vector, top_k: int) -> list[ScoredChunk]:
        self._ensure_table()
        query = sql.SQL(
            "SELECT id, document_url, document_title, text, position, heading, metadata,"
            " 1 - (embedding <=> %(q)s::vector) AS score"  # cosine distance → similarity
            " FROM {}.chunks ORDER BY embedding <=> %(q)s::vector LIMIT %(k)s"
        ).format(self._schema)
        with psycopg.connect(self._conninfo) as conn:
            rows = conn.execute(query, {"q": query_vector, "k": top_k}).fetchall()
        return [_to_scored_chunk(r) for r in rows]

    def keyword_search(self, query: str, top_k: int) -> list[ScoredChunk]:
        """Norwegian full-text search (stemming + stopwords) on the generated tsv column.

        plainto_tsquery joins words with AND (&), so a whole question would only match
        chunks containing every word. Swapping & for OR (|) means "any word matches, more
        matches rank higher", which suits natural-language questions.
        """
        self._ensure_table()
        sql_query = sql.SQL(
            "SELECT id, document_url, document_title, text, position, heading, metadata,"
            " ts_rank_cd(tsv, q.query) AS score"
            " FROM {}.chunks, (SELECT replace(plainto_tsquery('norwegian', %(q)s)::text,"
            " '&', '|')::tsquery AS query) AS q"
            " WHERE tsv @@ q.query ORDER BY score DESC, id LIMIT %(k)s"
        ).format(self._schema)
        with psycopg.connect(self._conninfo) as conn:
            rows = conn.execute(sql_query, {"q": query, "k": top_k}).fetchall()
        return [_to_scored_chunk(r) for r in rows]

    def _delete_sql(self) -> sql.Composed:
        return sql.SQL("DELETE FROM {}.chunks WHERE document_url = %s").format(self._schema)

    def _ensure_table(self) -> None:
        if self._ready:
            return
        create = sql.SQL(_CREATE).format(
            schema=self._schema, dimensions=sql.Literal(self._dimensions)
        )
        with psycopg.connect(self._conninfo) as conn:
            conn.execute(create)
            existing = conn.execute(
                "SELECT atttypmod FROM pg_attribute"
                " WHERE attrelid = %s::regclass AND attname = 'embedding'",
                (f"{self._schema_name}.chunks",),
            ).fetchone()
        if existing and existing[0] != self._dimensions:
            raise RuntimeError(
                f"{self._schema_name}.chunks stores {existing[0]}-dimensional vectors, but "
                f"EMBEDDING_DIMENSIONS is {self._dimensions}. Re-run indexing with --rebuild."
            )
        self._ready = True


def _to_scored_chunk(row: tuple[Any, ...]) -> ScoredChunk:
    """Columns: id, document_url, document_title, text, position, heading, metadata, score."""
    return ScoredChunk(
        Chunk(
            id=row[0],
            document_url=row[1],
            document_title=row[2],
            text=row[3],
            position=row[4],
            heading=row[5],
            metadata=row[6],
        ),
        score=float(row[7]),
    )
