## Context
Extraction ends at `{entity types offered in the candidate's prior} ∪ none` (`discriminated-union-extraction-decoding`). The offered branches come from a candidate's `TypePrior` (heading classifier + frequency floor `{Monster,Spell,Item,Class}`); `ExtractionUnionSchemaBuilder.Build(prior, schemas)` only builds a branch when the type is in the prior AND has a per-type schema. Non-entity content is declined (`entityType:none` from the LLM, or a pre-LLM `DeterministicTypeResolver` decline for a gated-prior no-5etools-match) and survives only as prose in `dnd_blocks`. `EntityType` ALREADY includes `Rule` (non-gated) and it maps 1:1 to block `ContentCategory.Rule` — but Rule is never in any candidate's prior AND has no per-type schema, so it is never offered. Result: **0 Rule entities in the entire corpus**, despite rules being ~50% of a book like the DMG.

## Goals / Non-Goals
**Goals (Phase 1 = Rule only):** rule content that is currently DECLINED gets admitted as a first-class `EntityType.Rule` entity, without flooding prose into fake rules; one taxonomy (`EntityType.Rule` ↔ `ContentCategory.Rule`). **Non-Goals:** Lore/Variant (Phase 2); the item-retyping half (`extraction-cross-type-recovery`); a new store or record type (Rule reuses the entity pipeline); wiring `ask_rules` to consume Rule entities (follow-on); a graph model.

## Decisions
- **D1 — Rule is a first-class entity, no new store.** A Rule classification is an `EntityType.Rule` entity flowing through the existing pipeline into `dnd_entities`: rule prose in `canonicalText`, minimal `RuleFields { summary?, ruleTopics?[] }` (`ruleTopics` aligns with `ask_rules`' per-rule grounding). Resolves the record-shape/storage question — no candidate-level side record.
- **D2 — Shared taxonomy.** `EntityType.Rule` ↔ block `ContentCategory.Rule` (same word), so the entity and retrieval layers agree.
- **D3 — Enablement is ALREADY DONE (reuse the 5etools Rule wiring).** The Rule pipeline already exists end-to-end for 5etools variantrules ingestion: `Schemas/canonical/RuleFields.schema.json` (`{ruleType?, entries}`) is on disk and `EntitySchemaProvider.LoadSchemas()` globs it, so `schemas[EntityType.Rule]` is already populated every run; `ExtractionPromptBuilder` has a `case EntityType.Rule`; `RuleCanonicalTextRenderer` is registered. So NO new schema/prompt/renderer code — reuse the existing `{ruleType?, entries}` shape (superseding the earlier `{summary?, ruleTopics?[]}` proposal). Rule is simply never OFFERED because it is never in a candidate's `TypePrior`; the whole change is putting it there for decline-bound+rule-signature candidates.
- **D4 — Rescue-would-be-declines gate (anti-flooding).** Rule is offered ONLY to a **decline-bound** candidate — one the `DeterministicTypeResolver` would decline (gated prior + no 5etools match), or that has no non-gated entity option — that ALSO carries a rule signature (substantial prose, non-entity-name heading, not a fragment/TOC). The single union call then lets the LLM pick `Rule` or `none`. Already-classified entities (Spell/Monster/…) are never touched; flooding is bounded to the decline pile (content otherwise lost), so even imperfect Rule-vs-none is strictly better than blanket decline. Fragments/TOC are rejected by the existing structural filters BEFORE Rule is offered, so noise cannot become a junk Rule.
- **D5 — Disposition.** A `Rule` pick is `Accepted` — grounded by its own book prose (non-gated → no 5etools name-match required). Only true noise still declines.

## Risks / Trade-offs
- **Rule flooding** → mitigated three ways: offered only to decline-bound candidates (never to real entities), only with a rule signature, and the LLM makes the final Rule-vs-none call; measured on the cheap harness before any live run.
- **qwen3 Rule-vs-none judgment ceiling** → acceptable: it operates only on content that is 100% declined today, so a wrong Rule pick replaces a total loss, and a wrong none pick is the status quo.
- **RuleFields too thin/rich** → keep minimal (summary + ruleTopics); the value is the entity EXISTING + grounded prose, not deep structure.
- **Validation cost** → cheap deterministic-path harness proves the GATE logic offline (no GPU); the full DMG re-extract (true quality proof) runs only on explicit go.

## Migration Plan
1. Add `RuleFields` + schema; add the rescue-gate to the orchestrator's decline path; keep it behind the existing structural filters (unit-green).
2. Cheap deterministic-path harness on real DMG candidates: confirm rule-signal decline-bound candidates now get Rule offered, real entities never do, decline rate would drop.
3. Full DMG re-extract ONLY on explicit go → diff entity-type distribution (Rule 0→N; entities unchanged; no flooding), spot-check the Rule entities.
4. Rollback = revert code; no schema/DB migration (Rule entities are ordinary entities; not ingested until validated).

## Open Questions
- The exact **rule signature** (what marks a decline-bound candidate as rule-like) is tuned on the cheap harness — start permissive (substantial prose + non-entity-name heading + not a fragment) since the LLM's Rule-vs-none pick is the real gate, and tighten only if the harness shows flooding.
- Phase 2 (Lore/Variant) reuses this exact machinery with their own signatures + `ContentCategory` alignment; out of scope here.
