"""What the tools hand back to the model, typed so the SDK publishes each tool's output schema.

Strict about what goes out, lenient about what comes in: each `from_api` reads the API's JSON
with `.get()`, so a field the API drops or renames becomes None rather than a failed tool.
"""

from __future__ import annotations

from typing import Any, Literal

from pydantic import BaseModel, Field

Status = Literal["draft", "published"]


def _status(item: dict[str, Any]) -> Status:
    return "draft" if item.get("isDraft") else "published"


class Account(BaseModel):
    id: int | None
    name: str | None
    handle: str | None
    email_confirmed: bool | None

    @classmethod
    def from_api(cls, me: dict[str, Any]) -> Account:
        return cls(
            id=me.get("id"),
            name=me.get("name"),
            handle=me.get("handle"),
            email_confirmed=me.get("isEmailConfirmed"),
        )


class PostSummary(BaseModel):
    id: int | None
    title: str | None
    slug: str | None
    summary: str | None
    status: Status
    published_at: str | None
    has_thumbnail: bool
    has_trailer: bool

    @classmethod
    def from_api(cls, item: dict[str, Any]) -> PostSummary:
        return cls(
            id=item.get("id"),
            title=item.get("title"),
            slug=item.get("slug"),
            summary=item.get("summary"),
            status=_status(item),
            published_at=item.get("publishedAt"),
            has_thumbnail=item.get("thumbnail") is not None,
            has_trailer=item.get("trailer") is not None,
        )


class PostPage(BaseModel):
    page: int | None
    total_pages: int | None
    total: int | None
    posts: list[PostSummary]

    @classmethod
    def from_api(cls, result: dict[str, Any]) -> PostPage:
        return cls(
            page=result.get("page"),
            total_pages=result.get("totalPages"),
            total=result.get("totalItems"),
            posts=[PostSummary.from_api(item) for item in result.get("items", [])],
        )


class Post(BaseModel):
    id: int | None
    title: str | None
    slug: str | None
    summary: str | None
    status: Status
    repo_url: str | None
    demo_url: str | None
    updated_at: str | None
    published_at: str | None
    body: str | None = Field(description="Markdown.")

    @classmethod
    def from_api(cls, post: dict[str, Any]) -> Post:
        return cls(
            id=post.get("id"),
            title=post.get("title"),
            slug=post.get("slug"),
            summary=post.get("summary"),
            status=_status(post),
            repo_url=post.get("repoUrl"),
            demo_url=post.get("demoUrl"),
            updated_at=post.get("updatedAt"),
            published_at=post.get("publishedAt"),
            body=post.get("body"),
        )


class Media(BaseModel):
    id: int | None
    role: str = Field(description="attachment, thumbnail or trailer.")
    filename: str | None
    caption: str | None = Field(description="Alt text.")
    type: str = Field(description="File type, e.g. webp or mp4.")
    bytes: int | None
    markdown: str = Field(description="Put this in the post body to embed the file.")

    @classmethod
    def from_api(cls, item: dict[str, Any]) -> Media:
        alt = item.get("caption") or item.get("filename") or "asset"
        return cls(
            id=item.get("id"),
            role=str(item.get("role", "")).lower(),
            filename=item.get("filename"),
            caption=item.get("caption"),
            type=str(item.get("extension", "")).lower(),
            bytes=item.get("byteSize"),
            # The same embed the studio's asset manager inserts: a path on the API, resolved by
            # the site's Markdown renderer.
            markdown=f"![{alt}]({item.get('url', '')})",
        )


class PostWithMedia(Post):
    media: list[Media]


class MediaList(BaseModel):
    post_id: int
    media: list[Media]


class Readiness(BaseModel):
    post_id: int
    is_draft: bool | None
    ready: bool = Field(description="True when publish_post would succeed.")
    missing: list[str]


class UploadTicket(BaseModel):
    upload_url: str
    expires_at: str | None
    command: str = Field(description="Run it with the file's path in place of <path>.")
    max_image_mb: int
    max_video_mb: int
    after_upload: str
