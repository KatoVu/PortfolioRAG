"""A fake BlogRAG API behind httpx.MockTransport, and a signed-in tool context."""

from __future__ import annotations

import json
from collections.abc import Iterator
from typing import Any

import httpx
import pytest
from mcp.server.auth.middleware.auth_context import auth_context_var
from mcp.server.auth.middleware.bearer_auth import AuthenticatedUser
from mcp.server.auth.provider import AccessToken

from blograg_mcp.config import Settings
from blograg_mcp.server import build_server

TOKEN = "pfl_test-token"


class FakeApi:
    """Just enough of the API to exercise the tools, recording every request it sees."""

    def __init__(self) -> None:
        self.requests: list[httpx.Request] = []
        self.posts: dict[int, dict[str, Any]] = {}
        self.media: dict[int, list[dict[str, Any]]] = {}
        self.next_id = 1
        # The connection's mode, as the API reports and enforces it: "Author" or "Editor".
        self.scope = "Author"

    def add_post(self, *, is_draft: bool = True, **fields: Any) -> dict[str, Any]:
        post = {
            "id": self.next_id,
            "title": "A post",
            "slug": f"post-{self.next_id}",
            "summary": None,
            "body": "body",
            "isDraft": is_draft,
            "isFeatured": False,
            "repoUrl": None,
            "demoUrl": None,
            **fields,
        }
        self.posts[post["id"]] = post
        self.media[post["id"]] = []
        self.next_id += 1
        return post

    def handler(self, request: httpx.Request) -> httpx.Response:
        self.requests.append(request)

        if request.headers.get("authorization") != f"Bearer {TOKEN}":
            return httpx.Response(401)

        path, method = request.url.path, request.method
        parts = path.strip("/").split("/")

        if path == "/api/auth/me":
            return httpx.Response(
                200,
                json={
                    "id": 1,
                    "name": "Ada",
                    "handle": "ada",
                    "isEmailConfirmed": True,
                    "tokenScope": self.scope,
                },
            )

        if path == "/api/admin/posts" and method == "GET":
            items = list(self.posts.values())
            return httpx.Response(
                200, json={"items": items, "page": 1, "totalPages": 1, "totalItems": len(items)}
            )

        if path == "/api/admin/posts" and method == "POST":
            return httpx.Response(201, json=self.add_post(**json.loads(request.content)))

        if parts[:3] == ["api", "admin", "posts"] and len(parts) >= 4:
            post_id = int(parts[3])
            post = self.posts.get(post_id)
            if post is None:
                return httpx.Response(
                    404, json={"title": "Not found", "detail": f"Post '{post_id}' was not found."}
                )

            if len(parts) == 4 and method == "GET":
                return httpx.Response(200, json=post)

            full_editing = self.scope == "Editor"

            if len(parts) == 4 and method == "PUT":
                if not post["isDraft"] and not full_editing:
                    return httpx.Response(403, json={"detail": "This token can only edit drafts."})
                post.update(json.loads(request.content))
                return httpx.Response(200, json=post)

            if method == "DELETE" or parts[4:] == ["unpublish"]:
                if not full_editing:
                    return httpx.Response(
                        403, json={"detail": "This API token can never delete anything."}
                    )
                if parts[4:] == ["unpublish"]:
                    post["isDraft"] = True
                    return httpx.Response(200, json=post)
                if len(parts) == 4:
                    del self.posts[post_id]
                else:
                    media_id = int(parts[5])
                    self.media[post_id] = [m for m in self.media[post_id] if m["id"] != media_id]
                return httpx.Response(204)

            if parts[4:] == ["publish"]:
                post["isDraft"] = False
                return httpx.Response(200, json=post)

            if parts[4:] == ["media"]:
                return httpx.Response(200, json=self.media[post_id])

            if parts[4:] == ["media", "upload-tickets"]:
                return httpx.Response(
                    201,
                    json={
                        "uploadUrl": "https://api.example.com/api/uploads/pfu_ticket",
                        "expiresAt": "2026-10-15T12:15:00Z",
                        "maxImageBytes": 10_485_760,
                        "maxVideoBytes": 104_857_600,
                    },
                )

        return httpx.Response(404, json={"title": "Not found"})


@pytest.fixture
def fake_api() -> FakeApi:
    return FakeApi()


@pytest.fixture
def server(fake_api: FakeApi):
    settings = Settings(
        api_url="http://api.internal",
        public_api_url="https://api.example.com",
        public_url="https://mcp.example.com/mcp",
    )
    return build_server(settings, transport=httpx.MockTransport(fake_api.handler))


@pytest.fixture
def signed_in() -> Iterator[None]:
    """What the bearer middleware leaves behind for a request that carried a valid token."""
    reset = auth_context_var.set(
        AuthenticatedUser(AccessToken(token=TOKEN, client_id="blograg", scopes=["posts"]))
    )
    yield
    auth_context_var.reset(reset)
