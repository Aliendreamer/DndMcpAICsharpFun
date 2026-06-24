## Why

The structured-entity extraction layer is unreliable corpus-wide: it is *type-first* (a substring keyword classifier freezes the entity type **before** the LLM runs, then the LLM is grammar-constrained to fill that **one** schema with no way to decline or re-type). Measured impact (parent `prose-grounded-knowledge-model` design.md §A): **37.3% of Monster-typed entities carry fabrication/misclassification signals, and 77.6% of those passed with `needsReview=false`** — silently accepted. The fix (parent §D–G) is **Option C2**, now de-risked: the `oneof-decoding-spike` confirmed that a discriminated-union (`oneOf`) schema decodes reliably under the existing grammar-constrained path on `qwen3:8b`, the model selects the right branch, and — on the exact Draconic Ancestry case — it **declines instead of fabricating a `Monster`**. This change builds that honest extraction path.

## What Changes

- **Content-first type selection.** Replace the pre-LLM type lock with a **discriminated-union `oneOf`** schema: the model picks its own `entityType` from the offered branches **or declines** via a `{"entityType":"none","reason"}` branch — under the existing reliable `ForJsonSchema` decoding. The keyword classifier is demoted from authority to a **prior that prunes** the union (frequency-floor + empirical confusion set + always-`none`); a bad prune degrades to a decline, never a fabrication.
- **Independent grounding gate.** Add a gate that validates emitted fields against the source prose — a tiered cascade (Tier 0 OCR-normalized fuzzy match → Tier 1 embedding coarse type-check reusing `dnd_blocks` vectors → Tier 2 `qwen3:8b` judge on the residual only). Ungrounded output is routed to review, never silently accepted. **Phased:** Tier 0 + the gate structure first; Tier 1/2 follow.
- **Disposition replaces the boolean.** Replace the `needsReview` bool with a per-entity **disposition** — `Accepted` / `NeedsReview` / `Declined` (logged, not dropped) / `Failed` — derived from grounding + decline + name/confidence, not from name + self-reported confidence alone.
- **BREAKING** (internal data shape): canonical-JSON entities gain a `disposition`; `declined` candidates are recorded (not silently dropped). The keyword classifier no longer determines the final type.

## Capabilities

### New Capabilities
- `content-first-extraction`: the LLM selects its `entityType` from a discriminated-union schema or declines; the keyword classifier becomes a union-pruning prior; no forced fabrication.
- `extraction-grounding-gate`: independent validation of emitted fields against source prose via a tiered cascade; ungrounded entities are flagged, not accepted.
- `extraction-disposition`: a per-entity disposition enum (Accepted/NeedsReview/Declined/Failed) as the trust signal, superseding the `needsReview` boolean.

### Modified Capabilities
- `entity-extraction-pipeline`: the LLM is constrained by a discriminated-union (pick-or-decline) schema rather than a single pre-selected per-type schema; the model determines the type or declines instead of being forced into a keyword-guessed type; `confidence` feeds the disposition rather than a boolean flag.

## Impact

- **Code:** `EntityCandidateScanner` / `MapCategoryToEntityType` (type stops being frozen here), `EntitySchemaProvider` (build the pruned union schema), `CandidateExtractor` / `OllamaEntityExtractionClient` (single union request, parse the discriminator), `EntityExtractionOrchestrator` (disposition + grounding gate + record declines), `HeadingCategoryClassifier` (becomes a ranked prior), `ExtractionNeedsReview` (→ disposition derivation). New grounding-gate components.
- **Data:** canonical-JSON entity shape gains `disposition`; declined candidates recorded. Existing canonical files remain readable (missing `disposition` defaults to `Accepted` for already-reviewed data).
- **Downstream (delta specs to follow as wiring lands):** `needsreview-triage` (the review list keys off disposition + new reasons: ungrounded, declined), `llm-extraction` (extract-entities behavior). Out of scope here: the richer knowledge model (tables/choice-sets/relationships — parent Slice 1), corpus re-extraction, the full 22-branch union scale-out (gated by classifier-as-prior pruning measurement).
- **Dependencies:** Ollama + `qwen3:8b` (confirmed reachable via the project's configured client); embeddings reuse `mxbai-embed-large` already in the pipeline.
