"""Verifies the bearer token on each MCP request by asking the API who it belongs to.

The tokens are opaque — the API's own `pfl_` tokens, issued by its OAuth flow — so the
API is the only thing that can say whether one is live. Answers are cached briefly, so a
burst of tool calls costs one round trip rather than one each.
"""

from __future__ import annotations

import hashlib
import logging
import time

import httpx
from mcp.server.auth.provider import AccessToken

logger = logging.getLogger(__name__)

SCOPE = "posts"


class ApiTokenVerifier:
    def __init__(
        self,
        api_url: str,
        *,
        cache_seconds: int = 30,
        timeout: float = 10.0,
        transport: httpx.AsyncBaseTransport | None = None,
    ):
        self._api_url = api_url.rstrip("/")
        self._cache_seconds = cache_seconds
        self._timeout = timeout
        self._transport = transport
        self._cache: dict[str, tuple[float, AccessToken]] = {}

    async def verify_token(self, token: str) -> AccessToken | None:
        # NOTE: keyed by a hash so the raw token is not sitting in a dict for a heap dump.
        key = hashlib.sha256(token.encode()).hexdigest()
        now = time.monotonic()

        cached = self._cache.get(key)
        if cached and cached[0] > now:
            return cached[1]

        async with httpx.AsyncClient(timeout=self._timeout, transport=self._transport) as client:
            try:
                response = await client.get(
                    f"{self._api_url}/api/auth/me",
                    headers={"Authorization": f"Bearer {token}"},
                )
            except httpx.HTTPError:
                logger.warning("Could not reach the API to verify a token.", exc_info=True)
                return None

        if response.status_code != 200:
            self._cache.pop(key, None)
            return None

        author = response.json()
        access = AccessToken(
            token=token,
            client_id="blograg",
            scopes=[SCOPE],
            subject=str(author.get("id", "")),
        )

        self._prune(now)
        self._cache[key] = (now + self._cache_seconds, access)
        return access

    def _prune(self, now: float) -> None:
        if len(self._cache) < 1024:
            return
        for key in [k for k, (expires, _) in self._cache.items() if expires <= now]:
            del self._cache[key]
