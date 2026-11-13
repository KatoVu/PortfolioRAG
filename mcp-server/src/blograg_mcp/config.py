"""Where the API is, and where this server is reachable from outside."""

from functools import lru_cache

from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    model_config = SettingsConfigDict(env_prefix="BLOGRAG_MCP_", env_file=".env", extra="ignore")

    # How this server reaches the API. Inside the compose network that is the service name,
    # not the public hostname: no round trip out through Traefik and back.
    api_url: str = "http://localhost:5009"

    # The API's public origin — the OAuth issuer, and the base of the media URLs a post body
    # embeds. Must match the API's OAuth:Issuer to the character (RFC 8414 compares exactly).
    public_api_url: str = "http://localhost:5009"

    # This server's own public URL, MCP path included. It is the resource a client asks a
    # token for, so it must be in the API's OAuth:AllowedResources (by origin).
    public_url: str = "http://localhost:8000/mcp"

    host: str = "127.0.0.1"
    port: int = 8000

    # Host headers accepted, against DNS rebinding. Defaults to the host of public_url.
    allowed_hosts: list[str] = []

    # How long a token the API vouched for is trusted without asking again. Short, because
    # disconnecting an app in the studio should stop it about as fast as it can notice.
    token_cache_seconds: int = 30

    request_timeout_seconds: float = 30.0

    log_level: str = "INFO"


@lru_cache
def get_settings() -> Settings:
    return Settings()
