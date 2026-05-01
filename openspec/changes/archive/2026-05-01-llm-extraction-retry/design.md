## Context

`OllamaLlmEntityExtractor.ExtractAsync` sends a page's text to the LLM with `Format = "json"` and a structured prompt. Ollama's JSON grammar mode is not perfectly reliable — smaller models sometimes return natural language, unquoted keys, or truncated arrays. The current `catch (JsonException | InvalidOperationException)` block creates a fake `Rule` entity (`page_{n}_raw`) with raw page text as `description`, which causes valid-looking but semantically garbage records to flow into Qdrant.

## Goals / Non-Goals

**Goals:**
- Retry the same LLM call up to `LlmExtractionRetries` times before giving up
- Return `[]` on total failure — no garbage data saved
- Make retry count configurable via `IngestionOptions` without requiring a redeploy

**Non-Goals:**
- Prompt mutation on retry (same prompt each attempt — can be improved later)
- Exponential backoff or delay between retries (not needed for a local LLM)
- Retry on non-parse failures (network errors, cancellation — those propagate as-is)

## Decisions

**Retry loop inside `OllamaLlmEntityExtractor` (not a decorator)**
The retry count is small (1 by default) and the logic is simple. A decorator (`RetryingLlmEntityExtractor`) would be cleaner separation of concerns but adds unnecessary abstraction overhead for this use case. The loop can be extracted to a decorator later if retry logic grows more complex.

**`LlmExtractionRetries` in `IngestionOptions` (not `OllamaOptions`)**
Retry behaviour is a pipeline concern (how many attempts before skipping a page), not an Ollama client concern (timeouts, models). `IngestionOptions` already owns `MinPageCharacters` and `BooksPath` — retry count fits the same theme.

**Return `[]` on exhausted retries (not throw)**
Extraction failures are soft — a single page/type failing should not abort the entire book. Logging a warning and continuing matches the existing treatment of sparse pages.

## Risks / Trade-offs

- **Longer extraction time on bad pages** — each retry is a full LLM round-trip (~5–60s). With `LlmExtractionRetries: 1` and 300 pages, worst case adds ~300 extra calls. Mitigated by the fact that retries only fire on parse failure, which should be rare once TOC-guided extraction limits passes to one per page.
- **Silent data gaps** — pages where the LLM never returns valid JSON produce no entities. Logged at Warning so operators can detect systematic failure. Accepted trade-off over polluting data with garbage.
