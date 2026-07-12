## Context

The retrieval layer has two Qdrant collections: `dnd_blocks` (prose chunks) and `dnd_entities` (typed
structured entities projected from hand-correctable `books/canonical/<slug>.json`). The content-first
extraction structures most types well (monsters carry full stat blocks; spells carry damage/condition
tags) but emits **classes prose-only** (`fields:{entries:[…]}`) because a class is a huge multi-page
table. The 5etools helper machinery is entity-level: `EntityBackfillService.ComputeAsync` diffs canonical
entities by normalized name against the 5etools roster and **appends** whole missing entities
(`dataSource:"5etools-backfill"`), **skipping** entities already present — so it can add a missing
*entity* but never a missing *field*. `FivetoolsSourceRegistry` maps each `EntityType` to its 5etools
file(s); `FivetoolsMapperRegistry` maps each type to a mapper whose `BuildFields(entry) => entry.Clone()`
exposes the full 5etools record. Both already cover all types (the `5etools/import` used them).

## Goals / Non-Goals

**Goals:** patch only *missing structured fields* onto extraction canonical entities from the matching
5etools record, for any type, driven by a per-type allowlist; keep extraction the source of truth
(prose/content and any field extraction produced always win); make it durable (can't silently decay);
undo the wholesale `5etools/import` and restore the extraction-favored index.

**Non-Goals:** structuring classes via the LLM (that's what 5etools sidesteps); a prose-grounded entity
rework (parked `prose-grounded-knowledge-model`); enriching prose (`entries` stays extraction's);
homebrew/no-source-key books (clean no-op); any new persistence/migration/MCP surface.

## Decisions

**Parallel field-fill seam, not bolted onto the roster service.** New `EntityFieldFillService` mirrors
the proven `EntityBackfillService` shape but does a field-merge instead of an entity-append. *Alternative:*
add a "field mode" to `EntityBackfillService` — rejected; it muddies the roster diff and couples two
distinct concerns.

**Type-agnostic, config-driven allowlist.** The service is driven by the existing `FivetoolsSourceRegistry`
(roster per type) + `FivetoolsMapperRegistry` (fields per type); the *only* per-type input is a declarative
`type → {structured field names}` allowlist. Covering a type is an allowlist entry, no new code. Seed
allowlists for the relevant types (Class: `hd`/`classFeatures`/`subclassTitle`/`proficiency`/`spellcasting`/
`classTableGroups`; Subclass: `subclassFeatures`; Spell: `level`/`school`/`range`/`components`/`duration`/
`classes`; Monster: extraction-thin fields like `environment`/`traitTags`). Allowlists are **structured
mechanics only — never `entries`/prose.** *Alternative:* "fill any missing 5etools field" — rejected; it
would pull 5etools prose/cruft and blur the extraction-owns-content line.

**Merged into canonical, auto-run after extraction — deterministic, so it can't decay.** The fill writes
`<slug>.json`; `ingest-entities` stays the projection step (no extra ingest in the normal flow). It
auto-runs at the end of `extract-entities`. Because 5etools is static and the allowlist fixed, the fill is
a pure function of (canonical names + 5etools data): a `force` re-extract regenerates the prose canonical
and the auto-fill re-derives the identical result. *Alternative:* a sidecar file that survives re-extract
structurally — rejected as unnecessary given determinism; one file is simpler and the auto-run guarantees
re-application.

**Provenance + merge rules — extraction/human always win, idempotent.** The entity `dataSource` stays
extraction (e.g. `"llm"`) — extraction owns the entity; 5etools only patched fields. A per-entity
`fivetoolsFilledFields: [names]` records which allowlisted fields were 5etools-filled. Per allowlisted
field: **absent** → fill + record; **present and listed** → re-derive (deterministic, same value);
**present and not listed** → never touch (extraction produced it, or a human hand-added it); entity
`dataSource == "manual"` → skip the whole entity (existing hand-correction protection). This gives
auditability, idempotency (a second run is byte-identical), and the "extraction beats 5etools" guarantee
from one marker.

**Matching.** An extraction entity binds to its 5etools record by `EntityNameIndex.Normalize(name)` +
the book's edition, exactly as the roster backfill matches. No 5etools match → the entity is left as-is
(extraction stands alone).

## Risks / Trade-offs

- **[Overwriting a hand-correction]** → the merge only touches allowlisted fields that are absent or
  previously-5etools-filled (in `fivetoolsFilledFields`), and skips `manual` entities entirely; a
  hand-added field is never in the list, so it's never overwritten.
- **[Force re-extract drops the fill]** → mitigated by construction: the fill auto-runs at the end of
  every extract, and is a deterministic re-derivation, so the canonical is always re-enriched.
- **[Non-atomic canonical write corrupts the file]** → write temp + atomic rename (mirroring extraction's
  own atomic write), and assert the unique-id invariant + a `CanonicalJsonLoader` round-trip before
  trusting the file (dev-flow gate).
- **[Self-seeded test hides a real gap]** → a fixture that injects the allowlisted 5etools fields proves
  the merge code, not that the real corpus has them; add a spot-check that `5etools/class/*.json` actually
  carry `hd`/`classFeatures` (dev-flow gate).
- **[Cleanup churn]** → the wholesale-import undo is a one-time rebuild of `dnd_entities` from canonical;
  it re-embeds the affected entities once (bounded), touches no blocks and re-converts no PDFs.

## Migration Plan

No schema/data migration. Build order: (1) allowlist config + `EntityFieldFillService` (unit-tested merge
+ idempotency); (2) name-matching + real-5etools spot-check; (3) `POST /admin/books/{id}/fill-fields`
endpoint (+ `.http`/`.insomnia`); (4) auto-run wired into extract-entities completion; (5) one-time
cleanup — fill the core books + rebuild `dnd_entities` from canonical (drops the import strays); (6) live
verify. Rollback is a code revert; the canonical fill is re-derivable and the index re-buildable.

## Verification

Unit: the merge rules (absent→fill, present&listed→re-derive, present&unlisted→untouched, manual→skip)
over a fake canonical + fake 5etools record; idempotency (run twice → byte-identical canonical).
Canonical-rewrite gates: unique-id invariant + `CanonicalJsonLoader` round-trip on the filled file +
`entries` untouched. Real-data spot-check: the real `5etools/class/*.json` carry the allowlisted fields.
Build 0/0 + full `dotnet test` green. Live: after fill + rebuild, level-up grounds all classes from
**extraction** entities (`dataSource:"llm"` + filled fields) and a monster reads back as the extraction
version — hybrid restored end-to-end.
