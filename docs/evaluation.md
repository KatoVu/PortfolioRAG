# Evaluation, in detail

The harness lives in `rag/eval/` and runs against a fixture portfolio (`eval/datasets/portfolio.json`)
seeded into a scratch account and deleted afterwards. With `--container`, it runs in a throwaway
`pgvector` container migrated with the API's own migrations.

## Retrieval is measured separately, and for free

When an answer is bad, "the model reasoned badly about good passages" and "the model reasoned well about
passages that didn't contain the answer" look the same from outside, and they need opposite fixes. So
retrieval gets its own numbers, with no provider key, no network, and no cost. **CI runs it on every
push.**

| Metric | What it tells you | Why it's here |
|---|---|---|
| **recall@k** | Share of the labelled relevant sources that were retrieved. | It caps everything downstream: the assessor can't cite what retrieval never found. Fix this first. |
| **precision@k** | Share of retrieved passages that are relevant. | Tracks how much noise the assessor has to read through. |
| **MRR** | 1 / rank of the first relevant passage. | Shows whether the best evidence is at the top. |
| **nDCG** (binary gain) | Ranking quality against the ideal order. | The only metric that notices a change that **reorders** results without adding or losing any, which is exactly what tuning RRF or MMR does. Gain is binary because the labels are binary; graded relevance would mean inventing judgements nobody made. |

Retrieval-only runs report numbers and don't pass or fail. No single threshold is right for every
corpus, and a suite that fails on an arbitrary number is a suite people learn to bypass. `--k 3` tightens
the cutoff to show where recall starts to break.

## Generation is graded deterministically first

**Structural checks** (`eval/judge.py`) need no model and **fail the suite** when they break, because each
one is a guarantee the pipeline claims to make:

- every requirement extracted came back with a finding,
- no `met` or `partial` finding without evidence,
- no evidence with an empty quote,
- the report is grounded: it cited at least one passage.

**Expectation checks** assert what should stay stable across reruns: the verdict falls in a band, the
score falls in a range, certain keywords come back met, and certain keywords come back **missing**.
They never assert on wording. Generated prose isn't deterministic, and an eval that checks exact
sentences fails on every rerun until people stop reading it.

**The LLM judge** (`--judge`) grades only what has no formula, each on a 1 to 5 scale:

- **calibration**: would a reader predict the score from the summary alone?
- **specificity**: does it name concrete projects, companies and durations?
- **gap honesty**: are unmet requirements stated plainly and early?
- **usefulness**: does it explain *why* the verdict is what it is?
- **unsupported claims**: every statement that goes beyond the findings, quoted. Hedges like "likely
  has" still count.

The judge grades **the prose against the verified findings**, never its own opinion of the candidate.
That keeps it testing what it's supposed to test instead of redoing the analysis and disagreeing. It can
only move a report between **pass and warn**. A judge that could fail a build on its own would turn every
off day of the judge model into a red CI run.

## The golden set

Five cases in `eval/datasets/golden.jsonl`, each chosen to catch a specific failure:

| Case | What it tests |
|---|---|
| `storage-staff-strong` | A posting squarely on target. The system has to find the match and say so. |
| `streaming-senior-strong` | A different slice of the same portfolio. Retrieval has to follow the query instead of always surfacing the strongest project. |
| `ml-platform-partial` | Real overlap on infrastructure, none on modelling. The middle of the score range has to get used. |
| `embedded-firmware-partial` | The oldest, thinnest part of the portfolio. Dated experience has to be found and dated honestly. |
| **`mobile-adversarial-weak`** | **The important one.** It asks for iOS/Swift/HealthKit work the portfolio doesn't contain, against a portfolio full of adjacent-sounding material: embedded C, Bluetooth, low-power devices. A system that reasons "a strong systems engineer probably also..." fails. Expected: `weak`, score ≤ 25, and `swift`, `ios`, `healthkit`, `app store` all reported missing. |

## Reproducibility

- Runs are **seeded** (`--seed`, recorded in `run.json`). Retrieval is SQL and arithmetic, and ONNX inference on CPU is deterministic.
- The seed is passed to OpenAI, Gemini and Groq as a sampling seed (which those vendors describe as
  "mostly repeatable"). Anthropic accepts no seed, so full runs against it will vary between runs, and
  the eval is designed with that in mind.
- `--container` gives stable passage ids across runs, so prompts are identical run to run.
- Swapping the embedding model is a single command that doesn't touch your dev database:

  ```sh
  RAG_EMBEDDING_MODEL=BAAI/bge-base-en-v1.5 RAG_EMBEDDING_DIMENSIONS=768 \
    uv run rag-eval --container --retrieval-only
  ```
