"""Runs the server: `blograg-mcp`, or `python -m blograg_mcp`."""

import logging

import uvicorn

from .config import get_settings
from .server import build_app


def main() -> None:
    settings = get_settings()
    logging.basicConfig(level=settings.log_level.upper())
    uvicorn.run(
        build_app(settings),
        host=settings.host,
        port=settings.port,
        log_level=settings.log_level.lower(),
        # NOTE: behind Traefik the client address and scheme arrive in X-Forwarded-*.
        proxy_headers=True,
        forwarded_allow_ips="*",
    )


if __name__ == "__main__":
    main()
