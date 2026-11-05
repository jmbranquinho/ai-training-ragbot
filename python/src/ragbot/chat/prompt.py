"""Prompt building and answer parsing: pure functions, no LLM call.

The exact prompt text is pinned by testdata/chat/expected-prompt.txt, which the .NET version
must also produce, so both implementations send the model identical prompts.
"""

import json
import re
from collections.abc import Sequence

from ragbot.domain import Answer, Chunk, ScoredChunk

SYSTEM_PROMPT = """\
You are a helpful assistant for the website of Stavanger kommune, a Norwegian municipality.
Answer the user's question using ONLY the numbered sources you are given.

Rules:
- Answer in the language of the question, even though the sources are in Norwegian \
(an English question gets an English answer).
- Be concise: a few sentences or a short list.
- Every statement must come from the sources. Cite them with their numbers, like [1] or [2][3].
- If the sources do not contain the answer, say that you don't know (in Norwegian: "Jeg vet \
ikke.") and suggest contacting Stavanger kommune. Do not guess and do not use outside knowledge.
- Reply with JSON only, in this format:
  {"language": "<the language the question is written in>", \
"answer": "<your answer, written in that language, with [n] citations>", \
"sources": [<numbers of the sources you used>]}
  Use an empty sources list when you don't know."""
# Why the "language" field: a rule like "answer in the question's language" alone was not
# enough; with Norwegian sources the model often answered English questions in Norwegian.
# Making it name the language first, before writing the answer, fixed that in testing.


def build_messages(question: str, context: Sequence[ScoredChunk]) -> list[dict[str, str]]:
    """System + user messages in the OpenAI chat format: [{"role": ..., "content": ...}]."""
    sources = "\n\n".join(
        f"[{i}] (URL: {hit.chunk.document_url})\n{hit.chunk.text}"
        for i, hit in enumerate(context, start=1)
    )
    user = f"Sources:\n\n{sources}\n\nQuestion: {question}"
    return [{"role": "system", "content": SYSTEM_PROMPT}, {"role": "user", "content": user}]


def parse_answer(reply: str, context: Sequence[ScoredChunk]) -> Answer:
    """Turn the model's JSON reply into an Answer, keeping only sources that really exist.

    - Sources are ordered by first citation in the text, then any extra ones from the JSON
      "sources" list. Numbers outside 1..len(context) are dropped, duplicates removed.
    - [n] markers in the text are renumbered to match that order (the model numbers by
      context position, e.g. [1][4], while the user sees a list numbered 1, 2, ...);
      markers pointing at non-existent sources are removed.
    - An answer without any valid source counts as "unknown" (Answer.is_unknown), so a
      made-up citation can't make a hallucinated answer look supported.
    """
    try:
        data = json.loads(_strip_code_fence(reply))
    except ValueError:  # not JSON at all
        return Answer(text=reply.strip(), sources=[])

    if not isinstance(data, dict):  # e.g. a JSON list
        return Answer(text=reply.strip(), sources=[])
    text, numbers = data.get("answer"), data.get("sources", [])
    # Strict types: a string such as "1" is iterable in Python, so it would otherwise "work"
    valid = (
        isinstance(text, str)
        and isinstance(numbers, list)
        and all(isinstance(n, int) and not isinstance(n, bool) for n in numbers)
    )
    if not valid:  # JSON, but not the shape we asked for
        return Answer(text=reply.strip(), sources=[])
    assert isinstance(text, str) and isinstance(numbers, list)  # tells pyright the types

    cited_in_text = [int(m) for m in _CITATION.findall(text)]
    order: list[int] = []  # context numbers, in the order the user will see them
    for n in [*cited_in_text, *numbers]:
        if 1 <= n <= len(context) and n not in order:
            order.append(n)

    new_number = {old: new for new, old in enumerate(order, start=1)}

    def renumber(match: re.Match[str]) -> str:
        old = int(match.group(1))
        return f"[{new_number[old]}]" if old in new_number else ""

    sources: list[Chunk] = [context[n - 1].chunk for n in order]
    return Answer(text=_CITATION.sub(renumber, text).strip(), sources=sources)


_CITATION = re.compile(r"\[(\d+)\]")


def _strip_code_fence(reply: str) -> str:
    """Models sometimes wrap JSON in ```json ... ``` despite being asked not to."""
    match = re.fullmatch(r"\s*```(?:json)?\s*(.*?)\s*```\s*", reply, re.DOTALL)
    return match.group(1) if match else reply
