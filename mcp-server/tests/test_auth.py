"""The token verifier: the API decides, and a burst of calls costs one question."""

from __future__ import annotations

import httpx

from blograg_mcp.auth import ApiTokenVerifier
from conftest import TOKEN, FakeApi


async def test_a_token_the_api_accepts_is_verified_once_then_cached(fake_api: FakeApi):
    verifier = ApiTokenVerifier(
        "http://api.internal", transport=httpx.MockTransport(fake_api.handler)
    )

    first = await verifier.verify_token(TOKEN)
    second = await verifier.verify_token(TOKEN)

    assert first is not None and second is not None
    assert first.scopes == ["posts"]
    assert first.subject == "1"
    assert len(fake_api.requests) == 1


async def test_a_token_the_api_refuses_is_not_verified(fake_api: FakeApi):
    verifier = ApiTokenVerifier(
        "http://api.internal", transport=httpx.MockTransport(fake_api.handler)
    )

    assert await verifier.verify_token("pfl_revoked") is None


async def test_an_unreachable_api_verifies_nothing():
    def down(_: httpx.Request) -> httpx.Response:
        raise httpx.ConnectError("down")

    verifier = ApiTokenVerifier("http://api.internal", transport=httpx.MockTransport(down))

    assert await verifier.verify_token(TOKEN) is None
