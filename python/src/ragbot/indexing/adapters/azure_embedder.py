"""Embedder backed by an Azure OpenAI / Azure AI Foundry embedding deployment."""

from collections.abc import Sequence

from azure.identity import DefaultAzureCredential, get_bearer_token_provider
from openai import AzureOpenAI

from ragbot.indexing.ports import Vector

_API_VERSION = "2024-10-21"
_ENTRA_SCOPE = "https://cognitiveservices.azure.com/.default"


class AzureOpenAIEmbedder:
    def __init__(
        self,
        endpoint: str,
        deployment: str,
        dimensions: int,
        api_key: str | None = None,
        batch_size: int = 64,
    ) -> None:
        if not endpoint:
            raise ValueError("AZURE_OPENAI_ENDPOINT is not set (see .env.example)")
        if api_key:
            self._client = AzureOpenAI(
                azure_endpoint=endpoint, api_key=api_key, api_version=_API_VERSION
            )
        else:  # Entra ID: uses your `az login` session locally, managed identity in Azure
            token_provider = get_bearer_token_provider(DefaultAzureCredential(), _ENTRA_SCOPE)
            self._client = AzureOpenAI(
                azure_endpoint=endpoint,
                azure_ad_token_provider=token_provider,
                api_version=_API_VERSION,
            )
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
