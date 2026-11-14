# Guardrails around cost and access

Job-fit runs on the **author's own** LLM key, so the platform treats the author's money as something to
protect:

- **Keys are validated before they're stored**, using each vendor's cheapest authenticated endpoint
  instead of a trial completion the author would pay for. They're then encrypted at rest; the API and
  worker share the key through an interop-tested envelope.
- **A per-visitor daily limit, a monthly run cap, and a monthly USD budget.** Every job records its token
  usage and cost whether or not it succeeds, and the budget is enforced against that record.
- **Job-fit is opt-in.** It only appears on a portfolio when the author has a working key and chose to
  expose it. The UI states what a run costs, how many runs are left, and what it reads *before* the
  visitor runs it.
- **API tokens can't touch provider keys.** Those endpoints are session-only, so a leaked `pfl_…` token
  can at most search an index of already-published work.
- **Providers are adapters.** Anthropic, OpenAI, Gemini and Groq sit behind a four-member `ChatProvider`
  protocol. The pipeline never imports a vendor SDK. See `rag/src/rag/providers/README.md`.
