## Context

Two pipelines write entities into the `dnd_entities` Qdrant collection:

1. **5etools import** (`POST /admin/5etools/import`) — maps 5etools JSON files to `EntityEnvelope` using `EntityIdSlug.For(sourceKey, entityType, name)`. Produces correct entity types (Subclass, Spell, Monster, etc.), SRD flags, and keywords. FileHash = `"5etools:{sourceBook}"`.

2. **Canonical ingest** (`POST /admin/books/{id}/ingest-entities`) — loads `data/canonical/<book-slug>.json`, embeds entities, upserts. LLM-extracted data has rich `fields` JSON and `canonicalText` but historically typed everything as `Class` and used display-name-derived book slugs (`tasha`, `dungeon-master-s-guide`) instead of source key slugs (`tce`, `dmg14`).

Current problems:
- Random GUIDs as Qdrant point IDs → upsert never overwrites, every ingest adds new copies
- Mismatched book prefix slugs → `tce.subclass.foo` (5etools) vs `tasha.class.foo` (canonical) = two separate points
- Both pipelines store full entity data → duplicates bloat the collection and confuse semantic search

## Goals / Non-Goals

**Goals:**
- Single Qdrant point per logical D&D entity
- Entity IDs consistent across both pipelines: `{sourcekeyslug}.{typeslug}.{nameslug}`
- Best data from each source: canonical `fields`/`canonicalText`, 5etools `type`/`srd`/`keywords`
- Idempotent: re-running either pipeline is safe
- Existing canonical JSONs corrected without re-extraction

**Non-Goals:**
- Merging block-level (`dnd_blocks`) data
- Cross-book entity deduplication (same monster in multiple books stays separate)
- Automatic conflict resolution when both sources have contradicting field values

## Decisions

### D1: Deterministic Qdrant point UUID from entity ID

**Decision:** Use `Guid.CreateVersion5(namespace, UTF8(entityId))` as the Qdrant point ID.

**Why:** Qdrant upsert-by-ID is the only reliable deduplication primitive. Random GUIDs disable it entirely. Version 5 (SHA-1 based) gives stable UUIDs without coordination.

**Alternative considered:** Delete-then-insert per entity. Rejected — not atomic, risks data loss on crash mid-run.

### D2: Source key as book prefix slug

**Decision:** Add source key aliases to `EntityIdSlug.BookOverrides` (`"TCE"` → `"tce"`, `"PHB"` → `"phb14"`, `"DMG"` → `"dmg14"`, etc.). Both pipelines call `EntityIdSlug.For(...)` and now produce the same prefix.

**Why:** 5etools source keys are the authoritative book identifiers for WotC content. Year suffixes (`phb14`, `dmg14`) distinguish editions where source keys differ (`PHB` vs `XPHB`).

**Alternative considered:** Runtime ID rewriting at ingest time. Rejected — keeps canonical JSON and Qdrant out of sync; cross-references in FieldsJson payload still use old IDs.

### D3: Fix types via 5etools cross-reference, not re-extraction

**Decision:** `POST /admin/canonical/fix-types?book=<slug>` loads canonical JSON, matches each entity against 5etools data by `name + sourceBook`, adopts the correct `EntityType`, rewrites the `id` field and all internal cross-reference strings, saves in place.

**Why:** Re-extraction takes 5+ hours per book and may introduce new LLM errors. 5etools already has authoritative types. A one-time script is deterministic and reviewable.

**Alternative considered:** Heuristic type inference from `fields` JSON. Rejected — fragile, many edge cases.

### D4: Per-field merge at ingest time (read-modify-write)

**Decision:** `EntityIngestionOrchestrator` batch-fetches existing Qdrant points before upserting, applies `EntityMerger.Merge(canonical, existing)`, upserts the merged result.

**Field priority:**

| Field | Winner | Rationale |
| --- | --- | --- |
| `fields`, `canonicalText` | Canonical | Richer extraction from full PDF text |
| `firstAppearedIn` | Canonical | Extracted with page context |
| `type` | 5etools | Authoritative classification |
| `srd`, `srd52`, `basicRules2024` | 5etools | Legally verified flags |
| `keywords` | Max(canonical, 5etools) | Take whichever list is longer |
| `page` | 5etools if set, else canonical | 5etools page numbers are more reliable |
| `DataSource` | `"llm"` | Marks entity as canonical-reviewed |

**Why read-modify-write over separate reconcile endpoint:** Keeps the pipeline self-contained. A separate reconcile step would need to be run manually after every ingest.

**Alternative considered:** 5etools-first (canonical supplements). Rejected — canonical `fields` JSON is the primary value of this pipeline; it should always win on structured data.

## Risks / Trade-offs

- **Type mismatch edge cases** → Some entities have no 5etools equivalent (homebrew-adjacent, extracted-only). `fix-types` leaves them typed as `Class` if no match found; manual review required for those.
- **Read-modify-write latency** → Adds one Qdrant batch read per ingest run (~404 entities = 1-2 extra seconds). Acceptable given extraction already takes hours.
- **Existing Qdrant data is stale** → After this change, all books must be re-ingested to get correct IDs and merged data. Old points with random GUIDs and wrong IDs remain until re-ingest. Document this in migration plan.
- **Cross-reference strings in FieldsJson** → Internal cross-refs (e.g. `"tasha.class.foo"`) in the stored `FieldsJson` payload are not rewritten. They're human-readable context, not machine lookups — acceptable for now.

## Migration Plan

1. Deploy code changes (EntityIdSlug, deterministic UUIDs, EntityMerger, fix-types endpoint)
2. Run `POST /admin/5etools/import` to re-import all 5etools data with deterministic UUIDs
3. For each canonical book:
   a. `POST /admin/canonical/fix-types?book=<slug>` — correct types + IDs in JSON
   b. Review git diff of canonical JSON
   c. `POST /admin/canonical/validate` — confirm clean
   d. `POST /admin/books/{id}/ingest-entities` — re-ingest with merge
4. Verify via `GET /retrieval/entities/search` — no duplicate IDs in results

**Rollback:** No data is deleted during migration — old random-UUID points remain until overwritten. Rolling back the code restores previous behavior; old points are still queryable.

## Open Questions

- None — all decisions made during brainstorming session 2026-05-10.
