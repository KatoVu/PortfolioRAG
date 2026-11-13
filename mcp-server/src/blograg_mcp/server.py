"""The MCP tools: read the author's posts, write drafts, illustrate them, and publish on request.

There is deliberately no tool that deletes, unpublishes, or edits a live post. The API would
refuse each of them on this token anyway — see ApiTokenScope.Author — but a tool the model can
see invites it to try.
"""

from __future__ import annotations

import logging
from typing import Annotated, Any, Literal
from urllib.parse import urlparse

import httpx
from mcp.server.auth.middleware.auth_context import get_access_token
from mcp.server.auth.settings import AuthSettings
from mcp.server.mcpserver import MCPServer
from mcp.server.mcpserver.exceptions import ToolError
from mcp.server.transport_security import TransportSecuritySettings
from mcp_types import ToolAnnotations
from pydantic import Field
from starlette.applications import Starlette
from starlette.requests import Request
from starlette.responses import JSONResponse

from .api import ApiError, BlogRagApi
from .auth import SCOPE, ApiTokenVerifier
from .config import Settings

logger = logging.getLogger(__name__)

INSTRUCTIONS = """\
Writes technical blog posts to the user's BlogRAG portfolio — as drafts.

What this server can do: read the user's posts, create drafts, edit drafts, upload pictures and
videos to a draft (including its thumbnail and trailer), and publish a draft. It can never delete
anything, never edit or unpublish a post that is already live, and never replace an existing
thumbnail or trailer.

Workflow:
1. create_draft with the full Markdown body.
2. For every file: request_upload returns a one-time URL; send the file to it yourself with
   `curl -sS -F "file=@/path/to/file" "<upload_url>"`. The response is the stored media; its
   `markdown` (also in list_media) is what to put in the body to embed it.
3. update_draft to insert the embeds.
4. check_publish_readiness. A post needs a thumbnail (image, 16:9) and a trailer (image or video).
5. Only call publish_post when the user has explicitly asked you to publish in this conversation.
   Published posts become evidence in the user's recruiter-facing job-fit index.
"""

PostId = Annotated[
    int, Field(description="The post's numeric id, from list_posts or create_draft.")
]

READ = ToolAnnotations(read_only_hint=True, open_world_hint=False)
WRITE = ToolAnnotations(read_only_hint=False, destructive_hint=False, open_world_hint=False)


def build_server(
    settings: Settings, *, transport: httpx.AsyncBaseTransport | None = None
) -> MCPServer:
    """The MCP server. `transport` replaces the network under the API calls, for tests."""
    verifier = ApiTokenVerifier(
        settings.api_url, cache_seconds=settings.token_cache_seconds, transport=transport
    )

    server = MCPServer(
        name="blograg",
        title="BlogRAG",
        instructions=INSTRUCTIONS,
        version="0.1.0",
        token_verifier=verifier,
        auth=AuthSettings(
            issuer_url=settings.public_api_url,
            resource_server_url=settings.public_url,
            required_scopes=[SCOPE],
            # NOTE: the tokens are opaque and the verifier asks the API about each one; the API
            # does not report which resource a token was minted for, so this check cannot be
            # made here. The API itself only mints for its allowed resources.
            validate_token_resource=False,
        ),
        log_level=settings.log_level.upper(),  # type: ignore[arg-type]
    )

    def api() -> BlogRagApi:
        access = get_access_token()
        if access is None:
            raise ToolError("Not signed in to BlogRAG. Reconnect the BlogRAG MCP server.")
        return BlogRagApi(
            settings.api_url,
            access.token,
            timeout=settings.request_timeout_seconds,
            transport=transport,
        )

    async def call(fn: Any, *args: Any, **kwargs: Any) -> Any:
        async with api() as client:
            try:
                return await fn(client, *args, **kwargs)
            except ApiError as error:
                raise ToolError(error.message) from error

    # -- reading ------------------------------------------------------------------------

    @server.tool(annotations=READ)
    async def whoami() -> dict[str, Any]:
        """The BlogRAG account this connection writes to: name, handle, and portfolio URL path."""
        me = await call(lambda c: c.me())
        return {
            "id": me.get("id"),
            "name": me.get("name"),
            "handle": me.get("handle"),
            "email_confirmed": me.get("isEmailConfirmed"),
        }

    @server.tool(annotations=READ)
    async def list_posts(
        status: Literal["all", "drafts", "published"] = "all",
        search: Annotated[str | None, Field(description="Matches titles and summaries.")] = None,
        page: Annotated[int, Field(ge=1)] = 1,
    ) -> dict[str, Any]:
        """The user's posts, newest first, 20 a page. Use it to check whether a topic is already
        covered before drafting, and to learn the user's voice from what they have published."""
        is_draft = {"all": None, "drafts": True, "published": False}[status]
        result = await call(
            lambda c: c.list_posts(is_draft=is_draft, search=search, page=page, page_size=20)
        )
        return {
            "page": result.get("page"),
            "total_pages": result.get("totalPages"),
            "total": result.get("totalItems"),
            "posts": [_summary(item) for item in result.get("items", [])],
        }

    @server.tool(annotations=READ)
    async def get_post(post_id: PostId) -> dict[str, Any]:
        """One post in full: its Markdown body, links, status, and every attached file."""
        post = await call(lambda c: c.get_post(post_id))
        media = await call(lambda c: c.list_media(post_id))
        return {**_post(post), "media": [_media(item) for item in media]}

    @server.tool(annotations=READ)
    async def list_media(post_id: PostId) -> dict[str, Any]:
        """Files attached to a post, each with the Markdown that embeds it in the body."""
        media = await call(lambda c: c.list_media(post_id))
        return {"post_id": post_id, "media": [_media(item) for item in media]}

    @server.tool(annotations=READ)
    async def check_publish_readiness(post_id: PostId) -> dict[str, Any]:
        """Whether a draft can be published, and what is missing if not. A post needs a
        thumbnail and a trailer before the API will publish it."""
        post = await call(lambda c: c.get_post(post_id))
        media = await call(lambda c: c.list_media(post_id))
        roles = {item.get("role") for item in media}

        missing = [
            label
            for role, label in (("Thumbnail", "thumbnail"), ("Trailer", "trailer"))
            if role not in roles
        ]
        if not post.get("summary"):
            missing.append("summary (recommended, shown on the project card)")

        blocking = [item for item in missing if not item.startswith("summary")]
        return {
            "post_id": post_id,
            "is_draft": post.get("isDraft"),
            "ready": post.get("isDraft") is True and not blocking,
            "missing": missing,
        }

    # -- writing ------------------------------------------------------------------------

    @server.tool(annotations=WRITE)
    async def create_draft(
        title: Annotated[str, Field(min_length=3, max_length=200)],
        body: Annotated[str, Field(min_length=1, max_length=100_000, description="Markdown.")],
        summary: Annotated[
            str | None,
            Field(max_length=500, description="One or two sentences for the project card."),
        ] = None,
        slug: Annotated[
            str | None,
            Field(
                max_length=200,
                pattern=r"^[a-z0-9]+(?:-[a-z0-9]+)*$",
                description="URL slug; derived from the title when omitted.",
            ),
        ] = None,
        repo_url: Annotated[str | None, Field(max_length=500)] = None,
        demo_url: Annotated[str | None, Field(max_length=500)] = None,
    ) -> dict[str, Any]:
        """Creates a new post as a draft. Nothing is public until it is published."""
        payload = _clean(
            {
                "title": title,
                "body": body,
                "summary": summary,
                "slug": slug,
                "repoUrl": repo_url,
                "demoUrl": demo_url,
            }
        )
        post = await call(lambda c: c.create_post(payload))
        return _post(post)

    @server.tool(annotations=WRITE)
    async def update_draft(
        post_id: PostId,
        title: Annotated[str | None, Field(min_length=3, max_length=200)] = None,
        body: Annotated[str | None, Field(min_length=1, max_length=100_000)] = None,
        summary: Annotated[str | None, Field(max_length=500)] = None,
        slug: Annotated[
            str | None, Field(max_length=200, pattern=r"^[a-z0-9]+(?:-[a-z0-9]+)*$")
        ] = None,
        repo_url: Annotated[str | None, Field(max_length=500)] = None,
        demo_url: Annotated[str | None, Field(max_length=500)] = None,
    ) -> dict[str, Any]:
        """Changes a draft. Only the fields given change; the rest are kept. Refused for a
        published post — the user has to unpublish it from the studio first."""
        current = await call(lambda c: c.get_post(post_id))
        if not current.get("isDraft"):
            raise ToolError(
                "This post is published, and this connection can only edit drafts. Ask the user "
                "to unpublish it from the studio if it should change."
            )

        payload = {
            "title": title if title is not None else current.get("title"),
            "body": body if body is not None else current.get("body"),
            "summary": summary if summary is not None else current.get("summary"),
            "slug": slug if slug is not None else current.get("slug"),
            "repoUrl": repo_url if repo_url is not None else current.get("repoUrl"),
            "demoUrl": demo_url if demo_url is not None else current.get("demoUrl"),
            # Ignored by the API for this token; sent because the endpoint requires the field.
            "isFeatured": current.get("isFeatured", False),
        }
        post = await call(lambda c: c.update_post(post_id, _clean(payload)))
        return _post(post)

    @server.tool(annotations=WRITE)
    async def request_upload(
        post_id: PostId,
        role: Annotated[
            Literal["attachment", "thumbnail", "trailer"],
            Field(
                description=(
                    "thumbnail: the 16:9 still on the project card (image only). trailer: shown in "
                    "the post's banner (image or video). attachment: anything embedded in the body."
                )
            ),
        ],
        caption: Annotated[
            str | None,
            Field(max_length=200, description="Alt text. Describe what the image shows."),
        ] = None,
    ) -> dict[str, Any]:
        """A one-time URL to upload one file to a draft. Send the file yourself:
        curl -sS -F "file=@/path/to/file" "<upload_url>"
        The link expires in minutes and works once. PNG, JPEG, GIF, WebP, MP4 and WebM are
        accepted; pictures are re-encoded. A draft's thumbnail or trailer cannot be replaced
        once set."""
        ticket = await call(lambda c: c.create_upload_ticket(post_id, role.capitalize(), caption))
        url = ticket["uploadUrl"]
        return {
            "upload_url": url,
            "expires_at": ticket.get("expiresAt"),
            "command": f'curl -sS --fail-with-body -F "file=@<path>" "{url}"',
            "max_image_mb": round(ticket.get("maxImageBytes", 0) / 1_048_576),
            "max_video_mb": round(ticket.get("maxVideoBytes", 0) / 1_048_576),
            "after_upload": (
                "The response is JSON for the stored file. Embed an attachment in the body with "
                "![caption](/api/media/<id>/content), or take the `markdown` from list_media."
            ),
        }

    @server.tool(annotations=WRITE)
    async def set_media_caption(
        post_id: PostId,
        media_id: Annotated[int, Field(description="From list_media.")],
        caption: Annotated[str | None, Field(max_length=200)],
    ) -> dict[str, Any]:
        """Rewrites a file's caption, which is its alt text. Drafts only."""
        media = await call(lambda c: c.update_media(post_id, media_id, caption))
        return _media(media)

    @server.tool(
        annotations=ToolAnnotations(
            read_only_hint=False, destructive_hint=False, idempotent_hint=True, open_world_hint=True
        )
    )
    async def publish_post(
        post_id: PostId,
        user_confirmed: Annotated[
            bool,
            Field(
                description=(
                    "True only if the user explicitly asked, in this conversation, to publish this "
                    "post. Never infer it."
                )
            ),
        ],
    ) -> dict[str, Any]:
        """Publishes a draft to the user's public portfolio, where it is also indexed as evidence
        recruiters can cite. Only on the user's explicit instruction. This connection cannot
        unpublish it afterwards — only the user can, from the studio."""
        if not user_confirmed:
            raise ToolError(
                "Not published. Ask the user whether to publish this post, and call again with "
                "user_confirmed=true only if they say yes."
            )

        post = await call(lambda c: c.publish(post_id))
        return _post(post)

    return server


def build_app(settings: Settings) -> Starlette:
    server = build_server(settings)

    host = urlparse(settings.public_url).netloc
    allowed_hosts = settings.allowed_hosts or [host]

    app = server.streamable_http_app(
        streamable_http_path=urlparse(settings.public_url).path or "/mcp",
        # NOTE: stateless, JSON responses. Every tool is one request to the API and back, so
        # there is nothing to stream and no session worth pinning a client to one replica for.
        stateless_http=True,
        json_response=True,
        transport_security=TransportSecuritySettings(
            enable_dns_rebinding_protection=True,
            allowed_hosts=[*allowed_hosts, f"{settings.host}:*", "localhost:*", "127.0.0.1:*"],
            allowed_origins=[],
        ),
        host=settings.host,
    )

    async def healthz(_: Request) -> JSONResponse:
        return JSONResponse({"status": "ok"})

    app.add_route("/healthz", healthz, methods=["GET"])
    return app


# ---------------------------------------------------------------------------------------
# Shaping the API's answers for a model: fewer fields, plain names, nothing it cannot use.
# ---------------------------------------------------------------------------------------


def _clean(payload: dict[str, Any]) -> dict[str, Any]:
    return {key: value for key, value in payload.items() if value is not None}


def _summary(item: dict[str, Any]) -> dict[str, Any]:
    return {
        "id": item.get("id"),
        "title": item.get("title"),
        "slug": item.get("slug"),
        "summary": item.get("summary"),
        "status": "draft" if item.get("isDraft") else "published",
        "published_at": item.get("publishedAt"),
        "has_thumbnail": item.get("thumbnail") is not None,
        "has_trailer": item.get("trailer") is not None,
    }


def _post(post: dict[str, Any]) -> dict[str, Any]:
    return {
        "id": post.get("id"),
        "title": post.get("title"),
        "slug": post.get("slug"),
        "summary": post.get("summary"),
        "status": "draft" if post.get("isDraft") else "published",
        "repo_url": post.get("repoUrl"),
        "demo_url": post.get("demoUrl"),
        "updated_at": post.get("updatedAt"),
        "published_at": post.get("publishedAt"),
        "body": post.get("body"),
    }


def _media(item: dict[str, Any]) -> dict[str, Any]:
    caption = item.get("caption") or item.get("filename") or "asset"
    url = item.get("url", "")
    return {
        "id": item.get("id"),
        "role": str(item.get("role", "")).lower(),
        "filename": item.get("filename"),
        "caption": item.get("caption"),
        "type": str(item.get("extension", "")).lower(),
        "bytes": item.get("byteSize"),
        # The same embed the studio's asset manager inserts: a path on the API, resolved by
        # the site's Markdown renderer.
        "markdown": f"![{caption}]({url})",
    }
