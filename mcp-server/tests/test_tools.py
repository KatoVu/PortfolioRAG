"""The tools, against a fake API: what they send, and what they refuse to do."""

from __future__ import annotations

import json
from typing import Any

import pytest
from mcp.server.mcpserver.exceptions import ToolError

from conftest import FakeApi


async def call(server: Any, name: str, **arguments: Any) -> Any:
    result = await server.call_tool(name, arguments)
    text = result.content[0].text
    if result.is_error:
        raise ToolError(text)
    return json.loads(text)


async def test_there_is_no_tool_that_deletes_or_unpublishes(server):
    names = [tool.name for tool in await server.list_tools()]

    assert names
    assert not [
        name for name in names if "delete" in name or "unpublish" in name or "remove" in name
    ]


async def test_reading_tools_are_marked_read_only(server):
    tools = {tool.name: tool for tool in await server.list_tools()}

    for name in ("whoami", "list_posts", "get_post", "list_media", "check_publish_readiness"):
        assert tools[name].annotations.read_only_hint is True


async def test_a_call_without_a_token_is_refused(server):
    with pytest.raises(ToolError, match="Not signed in"):
        await call(server, "whoami")


async def test_create_draft_sends_only_the_fields_given(server, fake_api: FakeApi, signed_in):
    post = await call(server, "create_draft", title="Hello world", body="# Hi")

    sent = json.loads(fake_api.requests[-1].content)
    assert sent == {"title": "Hello world", "body": "# Hi"}
    assert post["status"] == "draft"


async def test_update_draft_keeps_what_it_was_not_given(server, fake_api: FakeApi, signed_in):
    existing = fake_api.add_post(
        title="Kept title", summary="Kept summary", repoUrl="https://x.dev"
    )

    updated = await call(server, "update_draft", post_id=existing["id"], body="new body")

    sent = json.loads(fake_api.requests[-1].content)
    assert sent["title"] == "Kept title"
    assert sent["summary"] == "Kept summary"
    assert sent["repoUrl"] == "https://x.dev"
    assert updated["body"] == "new body"


async def test_update_draft_refuses_a_published_post_before_asking_the_api(
    server, fake_api: FakeApi, signed_in
):
    live = fake_api.add_post(is_draft=False)

    with pytest.raises(ToolError, match="published"):
        await call(server, "update_draft", post_id=live["id"], body="rewrite")

    assert all(request.method != "PUT" for request in fake_api.requests)


async def test_publish_needs_the_users_explicit_confirmation(server, fake_api: FakeApi, signed_in):
    draft = fake_api.add_post()

    with pytest.raises(ToolError, match="Ask the user"):
        await call(server, "publish_post", post_id=draft["id"], user_confirmed=False)

    assert fake_api.posts[draft["id"]]["isDraft"] is True

    published = await call(server, "publish_post", post_id=draft["id"], user_confirmed=True)
    assert published["status"] == "published"


async def test_request_upload_asks_for_the_right_role_and_hands_back_a_command(
    server, fake_api: FakeApi, signed_in
):
    draft = fake_api.add_post()

    ticket = await call(
        server, "request_upload", post_id=draft["id"], role="thumbnail", caption="Card"
    )

    assert json.loads(fake_api.requests[-1].content) == {"role": "Thumbnail", "caption": "Card"}
    assert ticket["upload_url"].endswith("/api/uploads/pfu_ticket")
    assert ticket["command"].startswith("curl ")
    assert ticket["max_image_mb"] == 10


async def test_readiness_names_what_is_missing(server, fake_api: FakeApi, signed_in):
    draft = fake_api.add_post()
    fake_api.media[draft["id"]] = [{"id": 1, "role": "Thumbnail", "url": "/api/media/1/content"}]

    readiness = await call(server, "check_publish_readiness", post_id=draft["id"])

    assert readiness["ready"] is False
    assert "trailer" in readiness["missing"]
    assert "thumbnail" not in readiness["missing"]


async def test_media_comes_with_the_embed_the_studio_would_insert(
    server, fake_api: FakeApi, signed_in
):
    draft = fake_api.add_post()
    fake_api.media[draft["id"]] = [
        {"id": 9, "role": "Attachment", "caption": "Architecture", "url": "/api/media/9/content"}
    ]

    media = await call(server, "list_media", post_id=draft["id"])

    assert media["media"][0]["markdown"] == "![Architecture](/api/media/9/content)"


async def test_an_api_refusal_reaches_the_model_in_its_own_words(server, signed_in):
    with pytest.raises(ToolError, match="was not found"):
        await call(server, "get_post", post_id=404)
