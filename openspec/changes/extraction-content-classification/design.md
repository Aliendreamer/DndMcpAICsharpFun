## Context
Extraction ends at `{entity types} ∪ none` (`discriminated-union-extraction-decoding`, `extraction-disposition`). Non-entity content is declined and lives only as prose in `dnd_blocks` (categorized by `ContentCategory`, retrieved via `ask_rules`/`ask_setting_lore`). So the entity layer and the retrieval layer have SEPARATE, unshared notions of "what this content is."

## Goals / Non-Goals
**Goals:** classify every candidate to its true category (entity OR Rule/Lore/Table/Variant OR true noise); one shared taxonomy across entity + retrieval; only real noise is declined. **Non-Goals:** re-parsing prose (blocks already exist); the item-retyping half (stays in extraction-cross-type-recovery); a full graph model.

## Decisions
- **D1** — extend the union target with non-entity category branches, not a second pass. **D2** — reuse the block-level `ContentCategory` vocabulary so entity + retrieval align. **D3** — a Table classification links to the `CanonicalTable` (mineru-table-extraction) rather than duplicating it. **D4** — phase: Rule first (highest-value, subsumes the rules-policy item), then Lore/Variant. **D5** — keep grounding: a category classification whose text doesn't ground is still rejected (no fabricated rules).

## Risks / Trade-offs
- **Category flooding** (every paragraph becomes a Rule/Lore) → gate each category with a signature; measure deltas offline before shipping. **Overlap with block ContentCategory** → this adds a *candidate-level* structured record, not a re-derivation; reconcile, don't duplicate. **Scope** → foundational; phased delivery, each phase independently valuable.
