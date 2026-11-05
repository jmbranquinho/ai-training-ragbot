# RagBot: a RAG chatbot in Python and .NET

A prototype chatbot that answers questions about a website using RAG
(Retrieval-Augmented Generation). It is built **twice, side by side**, once in
Python and once in C#, to compare the two ecosystems. The assignment (in
Norwegian) is in [docs/assignment-F1-no.txt](docs/assignment-F1-no.txt).

```
python/   Python 3.13 + uv
dotnet/   .NET 10
docs/     assignment text and notes
```

## Prerequisites

- **Python side:** [uv](https://docs.astral.sh/uv/) (`winget install astral-sh.uv`). uv installs the right Python version for you.
- **.NET side:** .NET 10 SDK
- **VS Code:** open the repo root and install the recommended extensions when prompted.

## Local database (Docker)

Chunks and embeddings are stored in PostgreSQL with the
[pgvector](https://github.com/pgvector/pgvector) extension, run with Docker Compose
([docker-compose.yml](docker-compose.yml)). Any Docker engine works. This repo uses the free
Docker Engine inside WSL 2 (no Docker Desktop or Rancher Desktop needed).

### One-time setup (Windows, PowerShell)

```bash
wsl --install -d Ubuntu-24.04 --no-launch
```

```bash
wsl -d Ubuntu-24.04 -u root -- bash "/mnt/c/dev/ved/AI Training/scripts/wsl-install-docker.sh"
```

```bash
wsl --terminate Ubuntu-24.04
```

[scripts/wsl-install-docker.sh](scripts/wsl-install-docker.sh) installs Docker Engine from
Docker's official apt repository, enables it as a service, and stops Ubuntu from inheriting the
Windows `PATH` (otherwise Windows-side Docker tools, such as Rancher Desktop's, interfere).

### Daily use

WSL shuts Ubuntu down when it's idle, so start the database before running the apps:

```bash
wsl -d Ubuntu-24.04 -u root --cd "/mnt/c/dev/ved/AI Training" -- docker compose up -d --wait
```

Other commands follow the same pattern: replace `up -d --wait` with
`ps` (status), `down` (stop; data is kept in the `pgdata` volume), `down -v` (stop and
**delete** all data, e.g. to re-run [db/init/](db/init/)) or
`exec postgres psql -U ragbot` (SQL prompt).

| Setting | Value (local development only) |
|---|---|
| Host / port | `localhost:5433` |
| Database / user / password | `ragbot` / `ragbot` / `ragbot` |
| Schemas | `python` and `dotnet`: one per implementation, so they don't overwrite each other |

## Run it

### Python

```bash
cd python
uv sync                 # create .venv and install exact versions from uv.lock
uv run ragbot ingest --max-pages 5   # scrape 5 pages into python/data/raw/
uv run pytest           # tests
uv run ruff format .    # format
uv run ruff check .     # lint
uv run pyright          # type check
```

### .NET

```bash
cd dotnet
dotnet build                                  # also runs analyzers (warnings = errors)
dotnet run --project src/RagBot.Cli -- ingest --max-pages 5   # into dotnet/data/raw/
dotnet test                                   # tests
dotnet format                                 # format
```

## Architecture

The code is organized **by feature**, and each feature separates its
**ports** (interfaces: the "what") from its **adapters** (implementations: the "how").

| Feature | Responsibility | Python | .NET |
|---|---|---|---|
| domain | Document, Chunk, ScoredChunk, Answer | `ragbot/domain/` | `RagBot.Core/Domain/` |
| ingestion | scrape pages, store raw documents | `ragbot/ingestion/` | `RagBot.Core/Ingestion/` |
| indexing | chunk, embed, index | `ragbot/indexing/` | `RagBot.Core/Indexing/` |
| chat | retrieve and answer with an LLM | `ragbot/chat/` | `RagBot.Core/Chat/` |

There are two flows. They share nothing except the index:

- **Offline pipeline** (`ingest`): scrape → store raw → chunk → embed → index
- **Chatbot** (`chat`): question → retrieve top-k → LLM → answer with sources

### Raw page storage

`ingest` saves each page as `data/raw/<host>/<url path>.md`: a small metadata header
(url, title, section, updated_on, fetched_at, content_hash) followed by the markdown.
Both versions use exactly the same format, pinned by
[testdata/stavanger/stored-document.md](testdata/stavanger/stored-document.md), so they can
read each other's files.

Re-running `ingest` still downloads every page (the site sends no ETag/Last-Modified
headers), but it compares content hashes and only rewrites pages that changed. It reports
each page as **new**, **updated** or **unchanged**.

## Python ↔ .NET cheat sheet

| Concept | Python | .NET |
|---|---|---|
| Package manager / CLI | `uv` | `dotnet` + NuGet |
| Project file | `pyproject.toml` | `*.csproj` (+ `Directory.Build.props`) |
| Lock file | `uv.lock` | `packages.lock.json` (opt-in) |
| Isolated environment | `.venv/` per project | not needed (NuGet cache is global) |
| Interface | `typing.Protocol` (structural, no inheritance) | `interface` (nominal, `: IFoo`) |
| Immutable model | `@dataclass(frozen=True)` | `record` |
| Nullable | `str \| None` | `string?` |
| Lazy sequence | `Iterator[T]` + `yield` | `IEnumerable<T>` + `yield return` |
| Lint + format | `ruff` | Roslyn analyzers + `dotnet format` |
| Type checking | `pyright` (separate tool) | the compiler |
| Tests | `pytest` (`def test_*`, plain `assert`) | xUnit (`[Fact]`, `Assert.*`) |
| Entry point | `cli.py` → `[project.scripts]` | `Program.cs` |

One deliberate difference is that the .NET interfaces are `async` with
`CancellationToken`, which is idiomatic there. The Python ones are synchronous
for now, to keep things simple while learning; Python has `async`/`await` too.
