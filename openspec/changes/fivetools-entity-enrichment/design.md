## Context

`EntityIngestionOrchestrator` already batch-fetches existing Qdrant points (`GetByIdsAsync`) and calls `EntityMerger.Merge(canonical, existing)` per entity, where `canonical` is the LLM entity (base) and `existing` is whatever is already stored. `EntityMerger` today only carries over `srd/srd52/basicRules2024/keywords/page/type` and treats `fields`/`canonicalText` as "canonical always wins". The 5etools side (`FivetoolsSourceRegistry` + 18 `IFivetoolsEntityMapper`s) can map every `5etools/*.json` record into an `EntityEnvelope` with the correct id (slug aligned to our books via `fivetoolsSourceKey`). The missing piece is sourcing 5etools as the "existing" data from files (not a prior store import) and deep-merging `fields`.

## Goals / Non-Goals

**Goals:**
- Our prose stays; 5etools' clean structured values replace OCR noise; SRD flags + keywords + clean name come from 5etools.
- Enrichment-only: never add 5etools-only entities to `dnd_entities`.
- Deterministic, re-runnable via re-ingest; reviewable coverage metrics.

**Non-Goals:**
- Changing the bulk `/admin/5etools/import` path (kept as-is for full imports).
- Adding new entity types or mappers (all 18 exist).
- Fuzzy/name-based matching — id-exact only in v1 (unmatched is fine and reported).
- Re-embedding strategy changes beyond what ingest already does (canonicalText is re-rendered from merged fields).

## Decisions

**1. 5etools file index (new, read-only).** A `FivetoolsRecordIndex` builds an in-memory `id → EntityEnvelope` map by walking `FivetoolsSourceRegistry.AllEntries`, mapping each record via the existing mappers. Built lazily/once per ingest run, filtered to the source keys actually being ingested (e.g. only PHB records when ingesting PHB) for speed. It does NOT touch Qdrant. If the `5etools/` files are absent, the index is empty and ingestion proceeds unenriched (graceful degradation, logged).

**2. Merge source precedence.** During ingest, the "existing" envelope passed to `EntityMerger` is the 5etools file record when one matches by id; otherwise the entity ingests unenriched. (The store `GetByIdsAsync` stays for the existing "don't clobber manual / preserve prior" behavior; the 5etools file index is the enrichment source.)

**3. Deep `fields` merge (extend `EntityMerger`).** Replace the "canonical fields always win" rule with a recursive merge of the two `JsonElement` field objects:
- For each key, if the value is a **narrative key** (in a small per-entity-type allowlist — e.g. `entries`, `description`, `text`, and `*Entries`), OUR value wins (preserve prose).
- Otherwise (scalars, numbers, enums, tag arrays like `cr`, `level`, `ac`, `components`, `traitTags`, `type`), the 5etools value wins **when present and non-empty**; if 5etools lacks it, ours is kept.
- Objects are merged recursively by the same rules; non-narrative arrays are taken wholesale from 5etools when present.
This keeps the function pure (`JsonElement` in, merged `JsonObject`/`JsonElement` out) and independently testable.

**4. Name rule.** `name` ← 5etools' clean name when a 5etools match exists AND `canonical.DataSource != "manual"`; otherwise keep ours. (Protects hand-corrected names; lets 5etools fix the rest.) Re-derive id only if extraction id derivation already does — ids are stable/aligned, so do not re-slug here.

**5. Flags / keywords / page / type.** Unchanged from today (5etools wins flags; keywords union/longer; page existing-first; type existing-if-Class) — but now sourced from the file record.

**6. Reporting.** `EntityIngestionOrchestrator` returns/logs `{ enriched, matchedFivetools, unmatched }` per book so coverage is visible after a re-ingest.

## Risks / Trade-offs

- **Narrative-key allowlist completeness.** If a prose-bearing key is missing from the allowlist, 5etools could overwrite some of our text. Mitigation: start from the known schema keys (`entries`, `description`, `text`, and per-type prose fields), unit-test the common types (Spell, Monster, Class), and treat the allowlist as a one-line extension point. Accepted.
- **Imperfect id alignment.** OCR-garbled ids we didn't hand-fix won't match and stay un-enriched. This is acceptable and reported; the just-shipped cleanup already corrected the worst.
- **5etools structured value shape vs ours.** A 5etools field may use a different JSON shape than our schema for the same concept. The deep merge is key-by-key, so mismatched shapes simply don't collide (5etools key wins only where keys match). No schema coercion in v1.
- **BREAKING fields behavior.** Existing entities re-ingested will change structured values. This is the intent; the change is gated behind a re-ingest and reviewable by diffing entity payloads / spot-checks.
