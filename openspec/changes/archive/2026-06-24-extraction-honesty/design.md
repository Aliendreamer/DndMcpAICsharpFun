## Context

Today: `HeadingCategoryClassifier.Guess` (substring keyword match) → page-keyed `TocCategoryMap` → `MapCategoryToEntityType` freezes `candidate.Type` → `EntityExtractionOrchestrator:396` selects ONE schema → `OllamaEntityExtractionClient` constrains output with `ForJsonSchema(singleSchema)`. The model cannot decline or re-type; `ExtractionNeedsReview.Derive` inspects only name + self-confidence (parent `prose-grounded-knowledge-model` design.md §B). The `oneof-decoding-spike` proved the fix mechanism works: a discriminated-union `oneOf` schema decodes reliably on `qwen3:8b` and the model declines rather than fabricating a `Monster` (spike `findings.md`). This change implements C2 honesty on that foundation.

## Goals / Non-Goals

**Goals:**
- The model picks its `entityType` from a pruned union or declines; the keyword classifier becomes a prior, not the authority.
- An independent grounding gate stops silent acceptance of ungrounded fabrications (the 77.6% blind spot).
- A `disposition` (Accepted/NeedsReview/Declined/Failed) replaces the `needsReview` boolean; declines are recorded, not dropped.

**Non-Goals:**
- The richer knowledge model — tables, choice-sets, relationships, provenance (parent Slice 1).
- Corpus re-extraction; the resolution engine / read path; the full 22-branch union scale-out (gated by measuring prior-pruning recall).
- Changing the embedding model or Qdrant collections.

## Decisions

- **Union under existing decoding, not native tools (C2 not C1).** Keep `ForJsonSchema`; build the union in `EntitySchemaProvider`. Rationale: the spike validated this path; C1 native tool-calling is unreliable on an 8B local model. Alternative (C1) rejected.
- **Classifier-as-prior with always-`none`.** `HeadingCategoryClassifier` returns a ranked set; the union = frequency-floor ∪ guess+confusion-set ∪ `none`. Rationale: caps schema size/cognitive load; the always-`none` invariant makes a mis-prune degrade to a decline, never a fabrication (the key safety property). Confusion set seeded from the parent §A misclassification data.
- **Grounding gate as a tiered cascade, phased.** Tier 0 OCR-normalized fuzzy (cheap, every field) → Tier 1 embedding coarse type-check (reuse `mxbai`/`dnd_blocks` vectors) → Tier 2 `qwen3:8b` judge on the residual only. **Phase 1 ships Tier 0 + the gate seam + disposition; Phase 2 adds Tier 1/2.** Rationale: Tier 0 alone catches gross fabrication (zeroed stats, empty CR) cheaply; the expensive judge is bounded to the residual. Escalation rate is logged to size the judge cost.
- **Disposition enum, backward-compatible.** New `disposition` on the canonical entity; absent → treated as `Accepted` (prior reviewed data not regressed). Declines recorded in a sidecar/section for audit. Rationale: false-declines must be auditable; existing files must keep loading.
- **Type decided post-LLM.** `EntityCandidateScanner` stops mapping category→frozen type; it carries the prior. The orchestrator reads `entityType` from the model's chosen branch. Rationale: removes the pre-LLM lock that is the root cause.
- **Identity stays keyed on the keyword-primary type (resume-safe).** The checkpoint reconstructs `doneIds` from `extracted[].Id`, and the loop computes the pre-call `id` from `candidate.Type` (keyword primary). So the entity `Id` keeps that primary-type slug for stable checkpoint/resume identity; the authoritative type is the envelope's `Type` *field* (the model's selection). Tradeoff: the id-slug may encode a different type than `Type` after a re-type; re-slugging is a separate migration. Alternative (id keyed on selected type) rejected — it breaks pre-call dedup / resume.
- **Multi-chunk preserved.** Content-first selects the type from the first chunk's union call, then completes fields from remaining chunks using the selected type's per-type schema and merges — so long entities are not truncated to their first chunk. Alternative (single first-chunk call) rejected — it regressed oversized-entity extraction.

## Risks / Trade-offs

- [Full 22-branch union may bloat context / degrade selection on `qwen3:8b`] → prune via the prior; measure selection recall offline against the existing 3,804-entity corpus before scaling the union; spike only proved 4 branches.
- [Tier 2 judge doubles per-candidate latency on slow CPU] → run only on the post-Tier-0/1 residual; log escalation rate; Phase 2 gated on that number being acceptable.
- [Grounding false-rejects on heavy OCR noise] → Tier 0 normalizes known confusables (`fI.`→`ft.`); fuzzy thresholds length-aware; ungrounded → `NeedsReview` (human), never silent drop.
- [Model over-declines real entities (false-decline)] → declines are recorded + audited; tune prompt salience of `none`; the grounding gate is the backstop for the opposite error (false-accept).
- [Disposition migration] → additive field, default `Accepted`; no rewrite of existing canonical files required.

## Migration Plan

1. Land Phase 1 (union pick-or-decline + prior-pruning + disposition + Tier 0 grounding) behind the existing `extract-entities` path; existing canonical files load unchanged (disposition defaults to `Accepted`).
2. Validate on one book's extraction; compare disposition distribution against the parent §A fabrication signals.
3. Add Phase 2 (Tier 1/2 grounding) once escalation rate is measured.
4. Rollback: revert to single-schema decoding; the disposition field is additive and harmless if unused.

## Open Questions

- Prune width vs selection recall — measured offline against the 3,804-entity corpus before scaling the union.
- Where `Declined` records live in the canonical file (sidecar vs an entities section) — decide in Phase 1.
- Tier-1 type anchors: what "looks like a stat block" embeds against (carried from parent §G).
