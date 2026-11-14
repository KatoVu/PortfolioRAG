# The BlogRAG MCP server: a small stateless HTTP service in front of the API.

FROM python:3.13-slim AS builder

COPY --from=ghcr.io/astral-sh/uv:0.11.16 /uv /usr/local/bin/uv

ENV UV_COMPILE_BYTECODE=1 \
    UV_LINK_MODE=copy \
    UV_PYTHON_DOWNLOADS=never

WORKDIR /app

# Dependencies before source, so a change to a tool's wording does not re-resolve the world.
COPY pyproject.toml uv.lock ./
RUN uv sync --locked --no-install-project --no-dev

COPY src ./src
COPY README.md ./
RUN uv sync --locked --no-dev


FROM python:3.13-slim AS runtime

RUN useradd --create-home --uid 10001 mcp

WORKDIR /app

COPY --from=builder --chown=mcp:mcp /app /app

ENV PATH="/app/.venv/bin:$PATH" \
    PYTHONUNBUFFERED=1 \
    PYTHONDONTWRITEBYTECODE=1 \
    BLOGRAG_MCP_HOST=0.0.0.0 \
    BLOGRAG_MCP_PORT=8000

USER mcp

EXPOSE 8000

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD python -c "import urllib.request; urllib.request.urlopen('http://127.0.0.1:8000/healthz', timeout=3)" || exit 1

CMD ["blograg-mcp"]
