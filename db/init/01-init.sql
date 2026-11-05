-- Runs once when the database volume is first created (see docker-compose.yml).
-- Tables are NOT created here: each app creates its own in Step 5, once we know the
-- embedding model's vector size (e.g. 1536 for text-embedding-3-small).

CREATE EXTENSION IF NOT EXISTS vector;

CREATE SCHEMA IF NOT EXISTS python;
CREATE SCHEMA IF NOT EXISTS dotnet;
