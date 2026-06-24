## Why

The parent investigation (`prose-grounded-knowledge-model`, design.md §D) chose **Option C2** to fix corpus-wide misclassification/fabrication: let the extraction model *pick its entity type or decline* via a **discriminated-union (`oneOf`) schema** kept under the existing grammar-constrained decoding (`ChatResponseFormat.ForJsonSchema`), rather than switching to flaky native tool-calling (C1). That decision rests on one **unverified assumption**: that the Ollama → llama.cpp JSON-schema→GBNF path reliably constrains output to a large `oneOf` union AND that the local `qwen3:8b` model can select the correct branch. If `oneOf` decoding doesn't work, C2 is not buildable and the whole extraction-honesty milestone must be re-planned. This spike answers that go/no-go question for ~30 minutes of work before any C2 commitment.

## What Changes

- Add a **throwaway spike harness** (test or scratch console, not production wiring) that issues `ChatResponseFormat.ForJsonSchema(unionSchema)` requests against the configured Ollama `qwen3:8b`, where `unionSchema` is a small hand-written **discriminated union** (≈3 branches + a `{"entityType":"none"}` decline branch).
- Run it against a few real candidate texts (including the Dragonborn/Draconic-Ancestry prose that C2 must re-type away from `Monster`) and record: does it return **valid JSON** constrained to one branch? Does the model **pick the right branch**? Does it use the **decline** branch when given non-entity prose?
- Produce a **documented decision** (`findings.md`) — `oneOf` works / partially / fails — that either **confirms C2** or **falls back to C1 / a two-pass router** in the parent design.
- No production code changes, no new endpoints, no schema/model changes. The harness is removed or left as a skipped/manual test after the decision is recorded.

## Capabilities

### New Capabilities
- `discriminated-union-extraction-decoding`: a spike-scoped capability stating the acceptance criteria the discriminated-union decoding must meet (constrained-valid output, correct branch selection on known cases, working decline branch) and the recorded go/no-go decision that gates the parent's C2 extraction fix.

### Modified Capabilities
<!-- None. This spike adds no requirements to existing capabilities; it validates an assumption for a future change. -->

## Impact

- **Code:** a throwaway spike harness only (test project or a scratch runner), exercising the existing `OllamaEntityExtractionClient`/`IChatClient` path. No changes to `EntityExtractionOrchestrator`, schemas, or endpoints.
- **Dependencies:** requires a running Ollama with `qwen3:8b` pulled (already the configured `Ollama:ChatModel`). CPU-bound and slow per the hardware constraint, but only a handful of calls.
- **Downstream:** the recorded decision directly gates Option C2 in `prose-grounded-knowledge-model` design.md §D; a negative result redirects that parent change to C1 or a two-pass router before any extraction-honesty work begins.
