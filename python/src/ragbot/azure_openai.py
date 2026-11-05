"""Creates the Azure OpenAI client shared by the embedder and the answer generator."""

from azure.identity import DefaultAzureCredential, get_bearer_token_provider
from openai import AzureOpenAI

API_VERSION = "2024-10-21"
_ENTRA_SCOPE = "https://cognitiveservices.azure.com/.default"


def create_client(endpoint: str, api_key: str | None) -> AzureOpenAI:
    """API key if given, otherwise Entra ID (your `az login` locally, managed identity in Azure)."""
    if not endpoint:
        raise ValueError("AZURE_OPENAI_ENDPOINT is not set (see .env.example)")
    if api_key:
        return AzureOpenAI(azure_endpoint=endpoint, api_key=api_key, api_version=API_VERSION)
    token_provider = get_bearer_token_provider(DefaultAzureCredential(), _ENTRA_SCOPE)
    return AzureOpenAI(
        azure_endpoint=endpoint, azure_ad_token_provider=token_provider, api_version=API_VERSION
    )
