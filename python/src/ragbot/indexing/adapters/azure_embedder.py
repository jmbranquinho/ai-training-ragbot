"""Embedder backed by an Azure OpenAI / Azure AI Foundry embedding deployment."""

from collections.abc import Sequence

from openai import AzureOpenAI

from ragbot.indexing.ports import Vector


class AzureOpenAIEmbedder:
    def __init__(
        self, client: AzureOpenAI, deployment: str, dimensions: int, batch_size: int = 64
    ) -> None:
        self._client = client
        self._deployment = deployment
        self._dimensions = dimensions
        self._batch_size = batch_size

    def embed(self, texts: Sequence[str]) -> list[Vector]:
        vectors: list[Vector] = []
        for start in range(0, len(texts), self._batch_size):  # several texts per request
            batch = list(texts[start : start + self._batch_size])
            response = self._client.embeddings.create(
                model=self._deployment, input=batch, dimensions=self._dimensions
            )
            vectors.extend(item.embedding for item in sorted(response.data, key=lambda d: d.index))
        return vectors
