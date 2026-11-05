"""The chat use case: question → retrieve → answer with sources.

Two layers keep the bot from making things up:
1. If retrieval finds nothing, answer "I don't know" without calling the model at all.
2. Otherwise the model is told to answer only from the sources, and must cite them;
   an answer without valid citations counts as unknown (see prompt.parse_answer).
There is deliberately no similarity threshold: relevant questions scored as low as 0.45
(e.g. in English), so a fixed cut-off would reject good questions. Step 8 can measure it.
"""

import logging

from ragbot.chat.ports import AnswerGenerator, Retriever
from ragbot.domain import Answer

logger = logging.getLogger(__name__)

NO_CONTEXT_ANSWER = "Jeg vet ikke. Jeg fant ingen relevant informasjon om dette på nettsidene."


class ChatService:
    def __init__(self, retriever: Retriever, generator: AnswerGenerator, top_k: int = 5) -> None:
        self._retriever = retriever
        self._generator = generator
        self._top_k = top_k

    def ask(self, question: str) -> Answer:
        question = question.strip()
        if not question:
            raise ValueError("question is empty")

        context = self._retriever.retrieve(question, self._top_k)
        for i, hit in enumerate(context, start=1):
            logger.debug("context [%d] %.3f %s", i, hit.score, hit.chunk.id)
        if not context:
            return Answer(text=NO_CONTEXT_ANSWER, sources=[])

        return self._generator.answer(question, context)
