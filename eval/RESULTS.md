# Step 8: Evaluation results

Measured on 2026-11-05/06 against the 61 unique pages of `renovasjon-og-miljo`
(stavanger.kommune.no), with `text-embedding-3-small` (1536 dims) and `gpt-4.1-mini`.

## The test set

[questions.json](questions.json): 26 questions, shared by both implementations.

- **20 Norwegian** questions across 19 pages, phrased the way a resident would ask
  ("Hamsteren min døde. Hva gjør jeg med den?"), not copied from headings
- **3 English** questions (sofa, food waste, fireworks)
- **3 unanswerable** ones, where the right answer is "I don't know": kindergarten places and
  resident parking (other parts of the site, not scraped) and the football World Cup (off-topic)

Each question lists the page(s) holding the answer and a short reference answer with the key facts.

Reproduce:

```bash
uv run ragbot eval retrieval --schema python            # or: dotnet run --project src/RagBot.Cli -- eval retrieval
uv run ragbot eval answers --schema python --mode hybrid --top-k 5
```

Per-question results are written to `eval/results/` (git-ignored).

## 1. Retrieval: is the right page found, and at which rank?

23 answerable questions. A hit is a chunk from one of the expected pages. Each configuration has
its own index (schema), built with `ragbot index --schema <name> ...`.

| Index (chunker) | Mode | hit@1 | hit@3 | hit@5 | hit@10 | MRR |
|---|---|---|---|---|---|---|
| **headings, max 1500** (default) | vector | 83% | 100% | 100% | 100% | 0.906 |
| | keyword | 57% | 70% | 70% | 78% | 0.638 |
| | **hybrid** | **91%** | 96% | **100%** | 100% | **0.946** |
| headings, max 800 | vector | 91% | 100% | 100% | 100% | 0.949 |
| | hybrid | 87% | 96% | 100% | 100% | 0.924 |
| headings, max 3000 | vector | 83% | 96% | 100% | 100% | 0.893 |
| | hybrid | 87% | 96% | 100% | 100% | 0.917 |
| window 1000 / overlap 200 | vector | 91% | 96% | 96% | 100% | 0.939 |
| | hybrid | 91% | 100% | 100% | 100% | 0.957 |
| window 500 / overlap 100 | vector | 91% | 100% | 100% | 100% | 0.957 |
| | hybrid | 87% | 100% | 100% | 100% | 0.928 |

Keyword-only rows for the other indexes are in `eval/results/`. They are 39–70% at hit@1 and
always miss the three English questions.

**How many hits are needed to be sure the right page is included?** With the default index and
hybrid search, the right page was always within the **top 4**: 20 of 23 at rank 1, 2 at rank 2,
1 at rank 4. **top_k = 5** covers every question with a small margin. Plain vector search had
everything within the top 3.

**Parity check:** .NET's `eval retrieval` on its own schema gave exactly the same numbers as
Python for all three modes.

## 2. Answers: is the final answer right?

The whole chatbot answers each question, and an LLM judge (`gpt-4.1-mini`) grades it against
the reference: *correct* / *partial* / *incorrect*. Unanswerable questions count as correct only
if the bot says it doesn't know.

| Configuration | Correct | Partial | Incorrect | Cited a correct page | Unanswerable refused |
|---|---|---|---|---|---|
| headings 1500, hybrid, k=5 (Python) | 18 | 5 | 0 | 23/23 | 3/3 |
| headings 1500, hybrid, k=5 (.NET) | 19 | 4 | 0 | 23/23 | 3/3 |
| headings 1500, hybrid, k=5 (.NET, after the refusal-language fix) | **20** | 3 | 0 | 23/23 | 3/3 |
| headings 1500, **vector**, k=5 | 20 | 3 | 0 | 23/23 | 3/3 |
| headings 1500, hybrid, **k=3** | 15 | 8 | 0 | 22/23 | 3/3 |
| **window 1000**, hybrid, k=5 | 17 | 6 | 0 | 23/23 | **2/3** ❌ |
| **window 1000**, vector, k=5 | 17 | 5 | **1** ❌ | 22/23 | 3/3 |

**Run-to-run noise:** the same configuration (headings 1500, hybrid, k=5) scored 18, 19 and 20
in three runs. Even at temperature 0 the model output varies slightly, and one question is
4 percentage points of 23. **Differences of 1–2 correct answers are not meaningful.**

## Findings

1. **Retrieval is not the bottleneck.** With headings + hybrid, the right page is first for
   91% of the questions and always in the top 4. Most "partial" answers have the right source,
   but the answer leaves out a detail from the reference.

2. **Better retrieval numbers did not mean better answers.** Window chunking (1000/200) had the
   best retrieval MRR (0.957) but the worst answers. Window chunks have no heading context
   (no "Page > Heading" line), which caused two real failures:
   - **A hallucination (q24):** a climate-plan sentence about kindergarten places "near where
     you live" was turned into an answer to "how do I apply for a kindergarten place?". With
     heading chunks, the "Klima- og miljøplan > …" header shows the model it's unrelated.
   - **A false refusal (q09):** the hamster sentence was cut into a window that ranked below the top 5.

   This is why the evaluation has to measure end-to-end answers, not only retrieval.

3. **top_k = 3 is too few.** Correct answers fell from about 18–20 to 15, and one answer no
   longer cited the right page. Several answers need facts spread over two or three chunks of
   the same page. **top_k = 5 is the better trade-off.**

4. **Hybrid vs. vector is a tie at this size.** Hybrid puts the right page first more often
   (91% vs 83%), vector had 20 vs 18 correct answers in one run, and both are within the noise.
   Keyword search alone is clearly worse: it can't handle English questions or Norwegian
   compound words (`metallbeholderen` doesn't match "metall").

5. **The evaluation found a mistake in the test set, and in the website.** q05 (composting
   discount) was first graded *incorrect*: the bot said kr 1269, the reference said kr 1113. The
   bot was right. The fee page has the 2026 rate (kr 1269), while the composting page still shows
   the old rate (kr 1113). The reference was corrected, and both pages now count.

6. **The judge is useful, but not perfect.**
   - Despite an explicit instruction, it still sometimes lowers grades for *extra* correct
     details (e.g. q04).
   - It is the same model that wrote the answers, so it may favour its own style.
   - Some references contain details a resident would not miss (e.g. that plastic is sorted at
     IVAR's Sorterium), which turns otherwise-correct answers into "partial".

## Decision: keep the current defaults

| Setting | Value | Why |
|---|---|---|
| Chunker | **headings, max 1500** | best answers; the heading header prevents misreadings like q24 |
| Retrieval | **hybrid** | best hit@1 on this index; helps exact terms such as "brikke"; tie with vector on answers |
| top_k | **5** | always includes the right page (worst rank 4); k=3 lost answer quality |

No code change was needed: these were already the defaults.

## Next improvements

- **A bigger test set (50–100 questions), and several runs per configuration.** At 23 questions,
  one question is 4 points, so smaller effects can't be measured.
- **A stronger or different model as judge**, and references trimmed to the facts a resident needs.
- **Prefer up-to-date pages:** pass each source's `updated_on` date to the model and tell it to
  prefer the newest when sources disagree (the q05 case).
- **Merge very short chunks** (89 chunks under 200 characters) into their neighbours, then
  measure the effect with this evaluation.
- **Weighted fusion:** give keyword results less weight than vector results in RRF, so
  non-matching keyword noise (e.g. English questions) can't push irrelevant chunks up.

## Second site: the Satisfactory Wiki (.NET, `--site satisfactory`)

772 English articles (775 stored, 3 duplicates) → 7,677 chunks with `headings:1500`, in schema
`dotnet_satisfactory`. Test set: [questions-satisfactory.json](questions-satisfactory.json),
18 questions: 13 English and 2 Norwegian answerable, and 3 off-topic (run on 2026-11-06).

| Retrieval | hit@1 | hit@3 | hit@5 | hit@10 | MRR |
|---|---|---|---|---|---|
| vector | **100%** | 100% | 100% | 100% | **1.000** |
| keyword | 40% | 60% | 67% | 87% | 0.547 |
| hybrid | 67% | 87% | 100% | 100% | 0.786 |

| Answers (top_k=5) | correct | partial | incorrect | cited a correct page | off-topic refused |
|---|---|---|---|---|---|
| hybrid | 11/15 | 3/15 | 1/15 | 13/15 | 3/3 |
| **vector** | **13/15** | 2/15 | **0/15** | **15/15** | 3/3 |

**The opposite of Stavanger: keyword search hurts here.** A game wiki repeats the same names
on many pages and sections. For "What does it cost to build the Space Elevator?", the words
match the page's Trivia and History sections best. The facts are in the infobox, which says
"Ingredients", not "cost". Hybrid then answered "the cost is not specified" (the one
incorrect). Vector search ranks by meaning and found the infobox chunk first for every
question. Both partial grades in vector mode are the judge marking down extra correct details.

**Decision:** the default retrieval mode is now a per-site setting (`SiteProfile.DefaultMode`):
`vector` for the wiki, `hybrid` for Stavanger. The best mode depends on the content, which is
the reason to evaluate each site separately.
