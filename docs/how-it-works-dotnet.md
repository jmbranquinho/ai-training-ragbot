# How RagBot works (.NET)

RagBot has three stages. **Ingestion** copies the website to disk, **indexing** makes those
copies searchable, and the **chatbot** answers questions from the index. **Evaluation** is a
fourth activity that measures how well the chatbot works. Each command in
[Program.cs](../dotnet/src/RagBot.Cli/Program.cs) runs one of these.

All commands below are run from the `dotnet/` folder.

## The big picture

```
   OFFLINE (run now and then)                         ONLINE (every question)
 ┌───────────────────────────────┐               ┌─────────────────────────────┐
 │ INGESTION                     │               │ CHAT (ask / chat)           │
 │ website → markdown files      │               │ question                    │
 │   dotnet/data/raw/*.md        │               │   → retrieve chunks ────────┼──┐
 └──────────────┬────────────────┘               │   → prompt gpt-4.1-mini     │  │
                │                                │   → answer + sources        │  │
 ┌──────────────▼────────────────┐               └─────────────────────────────┘  │
 │ INDEXING                      │                                                │
 │ files → chunks → embeddings   │    Postgres + pgvector, schema "dotnet"        │
 │   → Postgres table ───────────┼──► table dotnet.chunks ◄───────────────────────┘
 └───────────────────────────────┘

 EVALUATION: runs the online part on 26 known questions and scores the results.
```

The only thing the offline and online sides share is the `dotnet.chunks` table. The scraper
knows nothing about the chatbot, and the chatbot knows nothing about scraping.

## Where things live

| Folder in `RagBot.Core` | Contents |
|---|---|
| `Domain/` | the core records: `Document` (a page), `Chunk`, `ScoredChunk`, `Answer` |
| `Ingestion/`, `Indexing/`, `Chat/`, `Evaluation/` | one folder per feature: its **use case** (e.g. `IngestService`), its **ports** (interfaces in `Ports/`), and its **adapters** (implementations in `Adapters/`) |
| `Settings.cs`, `AzureOpenAIClientFactory.cs` | configuration from `.env`, and the shared Azure client |

`Program.cs` is the **composition root**. For each command it reads the options, creates the
concrete adapters, plugs them into a use case, and runs it. That's the only place that knows
"this run uses Postgres and Azure"; everything else works through interfaces.

### Other websites: `--site`

Everything below describes the default site, Stavanger. With `--site satisfactory`, the same
commands work on the Satisfactory Wiki, and only the edges change:

- **Ingestion:** pages come from `MediaWikiAllPagesUrlSource` (the wiki's `Special:AllPages`
  list) instead of `SiteMapUrlSource`. `HttpScraper` gets a `MediaWikiPageParser` instead of a
  `StavangerPageParser`, through the `IPageParser` seam.
- **Everything else:** `SiteProfile` supplies the site-specific values. These are the prompt
  wording, English instead of Norwegian keyword search, the data folder, the database schema
  and the eval questions.

## 1. Ingestion: `ingest`

**Purpose:** copy web pages to disk as clean markdown, so nothing later has to touch the website.

```bash
dotnet run --project src/RagBot.Cli -- ingest --section renovasjon-og-miljo --max-pages 200
```

What happens, step by step:

1. **`Program.RunIngestAsync`** reads the options, creates an `HttpClient` with the
   `RagBot-training/0.1` user agent, and builds the pieces below.
2. **`RobotsPolicy.LoadAsync`** downloads `robots.txt`, which says which paths the site doesn't
   want scraped (`/finn`, `/api`, …).
3. **`SiteMapUrlSource.DiscoverAsync`** (the `IUrlSource` port) downloads the HTML site map
   `/nettstedskart/` and pulls out all links under the section (`--section all` means every
   link). It drops anything robots.txt disallows, and the site map page itself.
4. **`IngestService.RunAsync`**, the use case:
   - with `--only-new`, it drops URLs that are already stored
   - with `--max-pages`, it keeps the first N
   - then it asks the scraper for the pages
5. **`HttpScraper.FetchAsync`** (the `IScraper` port) downloads each page, **waiting 1 second
   between requests**. It skips non-HTML responses (like PDFs) and errors, and passes the HTML to:
6. **`StavangerHtml.ParsePage`**, which takes the `<main>` element, removes noise (share
   buttons, images, staff contact boxes, PDF viewers), unwraps teaser cards, and converts the
   HTML to markdown with ReverseMarkdown. It returns a `Document`: URL, title, section, the
   page's "Oppdatert" date, the markdown, and the fetch time.
7. **Back in `IngestService`**: it compares the page's **content hash** with the stored copy
   and marks it *new*, *updated* or *unchanged*. Only new and updated pages are written.
8. **`FileDocumentStore.SaveAsync`** (the `IDocumentStore` port) writes
   `data/raw/<host>/<url path>.md`: a metadata header (`url`, `title`, `section`, `updated_on`,
   `fetched_at`, `content_hash`) followed by the markdown. It writes to a `.tmp` file first and
   then swaps it in, so a crash can't leave a half-written file.

**Output:** `X new, Y updated, Z unchanged`. Nothing touches the database or Azure.

## 2. Indexing: `index`

**Purpose:** turn the stored pages into searchable chunks with embeddings in Postgres.

```bash
dotnet run --project src/RagBot.Cli -- index
```

1. **`Program.RunIndexAsync`** loads `Settings` from `.env` and creates `FileDocumentStore` (to
   read the pages), a chunker (`HeadingChunker` by default), `AzureOpenAIEmbedder`, and
   `PgVectorChunkIndex` (schema `dotnet`).
2. **`IndexService.RunAsync`**, the use case:
   1. **Reads every stored page.**
   2. **De-duplicates** them with `DocumentDeduplicator`: pages with identical content keep
      only the shortest URL. The site serves whole plans on dozens of URLs.
   3. **Asks the index which versions it already has** (`DocumentVersionsAsync`). A page's
      *version* is `"<chunker settings>|<content hash>"`, e.g. `headings:1500|be84e5…`.
   4. **For each page:**
      - **Same version already indexed:** skip it. This is why re-running `index` is almost free.
      - **Otherwise, chunk it:** `HeadingChunker` splits at markdown headings, splits long
        sections at paragraph breaks, and gives each chunk a context header like
        `Farlig avfall > Hvor levere > Trygg levering` plus metadata.
      - **Embed it:** `AzureOpenAIEmbedder` sends the chunk texts to `text-embedding-3-small`
        in batches of 64 and gets back one vector of 1536 numbers per chunk. Texts with
        similar meaning get similar vectors.
      - **Store it:** `PgVectorChunkIndex.ReplaceDocumentAsync` deletes the page's old chunks
        and inserts the new ones, **in one transaction**.
   5. **Deletes pages that disappeared** from the store (or became duplicates) from the index.
3. **The table**, created automatically on first use: one row per chunk with its text, URL,
   title, heading, the `embedding vector(1536)` column with an **HNSW index** for fast
   similarity search, and a generated Norwegian **full-text** column for keyword search.

**Output:** `N pages indexed (M chunks embedded), K unchanged, R removed`.

`chunk` is a dry run of the chunking step: it chunks the pages and prints statistics, without
Azure or the database. `update` simply runs `ingest` and then `index`.

## 3. The chatbot: `search`, `ask` and `chat`

### `search`: retrieval only, no language model

```bash
dotnet run --project src/RagBot.Cli -- search "Hvor leverer jeg maling?" --mode hybrid
```

1. **`MakeRetriever`** picks one of three retrievers, all implementing `IRetriever`:
   - **`VectorRetriever`:** embeds the question with the same embedding model, then finds the
     closest chunk vectors (`SearchAsync`, ordered by cosine similarity with pgvector's `<=>`
     operator).
   - **`KeywordRetriever`:** Norwegian full-text search (`KeywordSearchAsync`). It reduces
     words to their stem (`batteriene` matches `batterier`) and ignores stopwords.
   - **`HybridRetriever`** (the default): runs both, taking 20 candidates each, then merges
     them with **Reciprocal Rank Fusion** (`RankFusion`). A chunk ranked high in both lists wins.
2. It prints the top-k chunks with their scores.

### `ask`: the full RAG flow

```bash
dotnet run --project src/RagBot.Cli -- ask "Where can I get rid of an old sofa?"
```

1. **`MakeChatService`** wires `ChatService` with a retriever and `AzureOpenAIAnswerGenerator`.
2. **`ChatService.AskAsync`:**
   1. **Retrieves** the top 5 chunks.
   2. **If nothing was found**, returns "Jeg vet ikke…" without calling the model.
   3. **Otherwise**, hands the question and chunks to the generator.
3. **`PromptBuilder.BuildMessages`** builds the prompt:
   - a **system message** with the rules: answer only from the sources, in the question's
     language, cite as `[n]`, say you don't know otherwise, reply as JSON
     `{language, answer, sources}`
   - a **user message** with the numbered sources and the question
4. **`AzureOpenAIAnswerGenerator`** sends it to `gpt-4.1-mini` at temperature 0, in JSON mode.
5. **`AnswerParser.Parse`** checks the reply:
   - keeps only cited source numbers that actually exist
   - renumbers the `[n]` markers to match the printed list
   - treats an answer **without valid sources as "unknown"**
6. **`PrintAnswer`** prints the answer and the source list (title › heading, URL).

`chat` is the same thing in a loop: it creates the services once and asks question after
question until you enter an empty line.

## 4. Evaluation: `eval`

**Purpose:** replace "it seems to work" with numbers, so you can compare settings. It uses the
26 questions in [eval/questions.json](../eval/questions.json), each with the correct page(s)
and a reference answer.

### `eval retrieval`: is the right page found?

1. **`QuestionSet.Load`** reads the questions.
2. For each mode (vector, keyword, hybrid), **`EvaluationService.EvaluateRetrievalAsync`**
   retrieves the top 10 chunks for every *answerable* question.
3. **`RetrievalMetrics.FirstHitRank`** gives the position of the first chunk from a correct
   page (1 = first).
4. **`RetrievalMetrics.Summarize`** turns those positions into:
   - **hit@k:** how often the right page is in the top k
   - **MRR:** the average of 1/rank, where 1.0 means always first
5. The command prints a table, and saves the per-question ranks as JSON in `eval/results/`.

No language model is involved, so it's quick and repeatable.

### `eval answers`: is the final answer right?

1. **`EvaluationService.EvaluateAnswersAsync`** runs `ChatService.AskAsync` for every question,
   the exact same flow as `ask`.
2. It grades each answer:
   - **Unanswerable question:** correct only if the bot said it doesn't know.
   - **Answerable, but the bot said it doesn't know:** incorrect.
   - **Otherwise, the LLM judge decides:** `AzureOpenAIJudge` sends the question, the
     reference and the answer to `gpt-4.1-mini` with the `JudgePrompt` rules, and gets back
     `correct`, `partial` or `incorrect`, with a reason.
3. It also checks whether the answer **cited a correct page**.
4. It prints each question's grade plus a summary, and saves everything to `eval/results/`.

The results and the decisions they led to are in [eval/RESULTS.md](../eval/RESULTS.md).

## Why it's built this way

- **Swappable infrastructure.** The use cases (`IngestService`, `IndexService`, `ChatService`,
  `EvaluationService`) only know interfaces. That's why the tests can swap Postgres for
  `InMemoryChunkIndex`, Azure for `FakeEmbedder`, and the website for `FakeHttpHandler`, and
  run in seconds without a network.
- **Every stage can be re-run.** Ingestion compares content hashes, indexing compares versions,
  and both save page by page. An interrupted run loses nothing, and re-running only does the
  work that's actually needed.
