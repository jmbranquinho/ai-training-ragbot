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

## Run it

### Python

```bash
cd python
uv sync                 # create .venv and install exact versions from uv.lock
uv run ragbot ingest    # run the CLI
uv run pytest           # tests
uv run ruff format .    # format
uv run ruff check .     # lint
uv run pyright          # type check
```

### .NET

```bash
cd dotnet
dotnet build                                  # also runs analyzers (warnings = errors)
dotnet run --project src/RagBot.Cli -- ingest # run the CLI
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
