## Context

The extraction pipeline today (`OllamaEntityExtractionClient.cs:23`) constrains the model with `ChatResponseFormat.ForJsonSchema(singleTypeSchema)` — grammar-constrained decoding over **one** pre-selected type schema, so the model cannot decline or re-type (parent design.md §B). The C2 fix (parent §D) keeps this reliable decoding path but swaps the single schema for a **discriminated-union `oneOf`** over all type branches plus a `none` decline branch. The open risk: it is unverified that the Ollama → llama.cpp JSON-schema→GBNF conversion handles a large `oneOf` union, or that `qwen3:8b` selects branches well. This spike answers that before any C2 build. Hardware constraint applies (`qwen3:8b` is CPU-bound, slow per `project_gpu_extraction_constraint`), but the spike is only a handful of calls.

## Goals / Non-Goals

**Goals:**
- Determine empirically whether `ForJsonSchema` over a discriminated-union schema yields constrained-valid, single-branch output via the existing Ollama path.
- Observe branch-selection quality on a few known cases (a clean spell; the Dragonborn/Draconic-Ancestry prose that must NOT become `Monster`; a non-entity passage that should decline).
- Record a clear go/no-go decision (`findings.md`) that confirms or redirects C2 in the parent change.

**Non-Goals:**
- No production wiring, no changes to `EntityExtractionOrchestrator`, `EntitySchemaProvider`, schemas, or endpoints.
- Not building the real 22-branch union, the classifier-as-prior pruning, or the grounding gate — those are the C2 milestone, gated by this result.
- Not optimising latency or measuring escalation rates.

## Decisions

- **Throwaway harness, not production path.** Implement as a manual/skipped test (or scratch runner) that calls the existing `IChatClient`/`OllamaEntityExtractionClient` with a hand-written union schema. Rationale: the spike must touch the *real* decoding path (same `ForJsonSchema` mechanism, same model) to be valid, but must not entangle production code before the decision. Alternative considered — wiring a real union into `EntitySchemaProvider` — rejected as premature for an unproven mechanism.
- **Minimal 3-branch union + decline.** Use ~3 representative branches (e.g. `Spell`, `Race`, `Monster`) plus `{"entityType":"none","reason":string}`, with `entityType` as a `const` discriminator per branch. Rationale: large enough to exercise `oneOf` alternation and branch selection, small enough to author by hand and reason about. If 3 branches work, schema-size risk for the full union is a separate, measurable concern for C2 (not this spike).
- **Real candidate texts, including the known failure.** Feed the actual Draconic Ancestry / Dragonborn prose (from `books/conversion-cache/` per parent design §C) plus a clean spell and a non-entity heading. Rationale: the spike must test the exact case C2 exists to fix, not synthetic input.
- **Decision taxonomy:** `findings.md` records one of **C2 confirmed** / **C2 conditional** (works only with prior-pruning to shrink the union) / **C2 rejected** (→ C1 native tool-calling or two-pass router). Rationale: a binary pass/fail loses the most likely real outcome (works but needs pruning).

## Risks / Trade-offs

- **`oneOf` silently degrades** (e.g. llama.cpp accepts the schema but the grammar lets fields leak across branches) → harness explicitly validates that output matches exactly one branch with no cross-branch fields, not just that JSON parses.
- **Small-model branch errors confound the mechanism test** → separate the two questions in `findings.md`: (a) does decoding *constrain* correctly (mechanism), (b) does the model *choose* correctly (capability). C2 viability needs (a); (b) informs whether prior-pruning is required.
- **Single-run variance on a slow model** → run each case a few times; record consistency, not a single sample. Keep total calls small given CPU-bound latency.
- **Harness rots into dead code** → remove or mark skipped/manual after `findings.md` is written; the decision is the deliverable, not the harness.

## Open Questions

- If C2 is confirmed, the *full* 22-branch union's schema size / context cost on `qwen3:8b` is still unmeasured — explicitly deferred to the C2 milestone (parent §F, classifier-as-prior pruning).
- Does branch quality vary enough by prompt phrasing to need the parent's re-enabled type-routing guidance? Note but do not chase here.
