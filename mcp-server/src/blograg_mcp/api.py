"""A thin client over the BlogRAG API, called with the assistant's own token.

Every rule — drafts only, no deletes, no replacing artwork — is enforced by the API on that
token. Nothing here is a second line of defence that could drift from the first; it only
turns the API's answers into something a model can act on.
"""

from __future__ import annotations

from typing import Any

import httpx


class ApiError(Exception):
    """An API refusal, carrying the API's own explanation."""

    def __init__(self, status: int, message: str):
        super().__init__(message)
        self.status = status
        self.message = message


class BlogRagApi:
    def __init__(
        self,
        base_url: str,
        token: str,
        *,
        timeout: float = 30.0,
        transport: httpx.AsyncBaseTransport | None = None,
    ):
        self._client = httpx.AsyncClient(
            base_url=base_url.rstrip("/"),
            headers={"Authorization": f"Bearer {token}", "Accept": "application/json"},
            timeout=timeout,
            transport=transport,
        )

    async def __aenter__(self) -> BlogRagApi:
        return self

    async def __aexit__(self, *_: object) -> None:
        await self._client.aclose()

    async def request(self, method: str, path: str, **kwargs: Any) -> Any:
        try:
            response = await self._client.request(method, path, **kwargs)
        except httpx.HTTPError as error:
            raise ApiError(0, f"Could not reach the BlogRAG API: {error}") from error

        if response.is_success:
            return response.json() if response.content else None

        raise ApiError(response.status_code, _describe(response))

    # -- account ------------------------------------------------------------------------

    async def me(self) -> dict[str, Any]:
        return await self.request("GET", "/api/auth/me")

    # -- posts --------------------------------------------------------------------------

    async def list_posts(
        self, *, is_draft: bool | None, search: str | None, page: int, page_size: int
    ) -> dict[str, Any]:
        params: dict[str, Any] = {"page": page, "pageSize": page_size}
        if is_draft is not None:
            params["isDraft"] = str(is_draft).lower()
        if search:
            params["search"] = search
        return await self.request("GET", "/api/admin/posts", params=params)

    async def get_post(self, post_id: int) -> dict[str, Any]:
        return await self.request("GET", f"/api/admin/posts/{post_id}")

    async def create_post(self, body: dict[str, Any]) -> dict[str, Any]:
        return await self.request("POST", "/api/admin/posts", json=body)

    async def update_post(self, post_id: int, body: dict[str, Any]) -> dict[str, Any]:
        return await self.request("PUT", f"/api/admin/posts/{post_id}", json=body)

    async def publish(self, post_id: int) -> dict[str, Any]:
        return await self.request("POST", f"/api/admin/posts/{post_id}/publish")

    # -- media --------------------------------------------------------------------------

    async def list_media(self, post_id: int) -> list[dict[str, Any]]:
        return await self.request("GET", f"/api/admin/posts/{post_id}/media")

    async def create_upload_ticket(
        self, post_id: int, role: str, caption: str | None
    ) -> dict[str, Any]:
        return await self.request(
            "POST",
            f"/api/admin/posts/{post_id}/media/upload-tickets",
            json={"role": role, "caption": caption},
        )

    async def update_media(
        self, post_id: int, media_id: int, caption: str | None
    ) -> dict[str, Any]:
        return await self.request(
            "PUT", f"/api/admin/posts/{post_id}/media/{media_id}", json={"caption": caption}
        )


def _describe(response: httpx.Response) -> str:
    """The most useful sentence in an error response: problem+json detail, then title."""
    try:
        problem = response.json()
    except ValueError:
        problem = None

    if isinstance(problem, dict):
        errors = problem.get("errors")
        if isinstance(errors, dict):
            for messages in errors.values():
                if messages:
                    return str(messages[0])
        for key in ("detail", "title", "error_description"):
            value = problem.get(key)
            if isinstance(value, str) and value:
                return value

    return f"The BlogRAG API answered {response.status_code}."
