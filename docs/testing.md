# Tests

```sh
dotnet test                                   # API, against a real Postgres
cd rag && uv run pytest                       # unit: no DB, no network, no cost
cd rag && uv run pytest -m integration        # + migrated Postgres
cd rag && uv run rag-eval --retrieval-only    # free retrieval metrics
cd rag && uv run rag-eval --judge             # full pipeline + judge (asks before spending)
cd mcp-server && uv run pytest -q             # MCP server, against a fake API
```

## Unit tests cover the adversarial paths

A real model won't reliably fabricate a citation when you ask it to, so `tests/test_pipeline.py` runs the
whole pipeline against a **stubbed provider** that does: it cites passages that were never shown and
invents quotes. The unit suite also pins chunking, RRF, MMR, scoring, citation verification, and the
C#/Python encryption interop against a ciphertext checked in from the .NET side.
