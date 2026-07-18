## Context

`entity-set-query` (shipped) gives `list_entities` complete filter-set retrieval over the **indexed** `dnd_entities` payload fields (type, cr, spell_level, damage_type, keyword, source, srd). But spell↔class is not an indexed field and — critically — is not even on our spell entities: 5etools stores it in the reverse index `5etools/spells/sources.json`, not on the spell object. So a "spells a Wizard can learn" join can't be answered from the entity store alone. The relationship data, however, is complete and static on disk (801/920 spell-entries, all 10 caster classes).

## Goals / Non-Goals

**Goals:**

- Answer "spells castable by class C" (optionally + spell level / school / source) as a **complete** set, deterministically.
- Zero data migration: no canonical-JSON fill, no `dnd_entities` re-projection, no Qdrant payload change, no GPU — the join is computed at query time from `sources.json`.

**Non-Goals:**

- Subclass-granularity ("only Eldritch Knight spells") — the index has subclass data but this slice does class-level only; subclass is a later refinement.
- Race/attribute aggregation and table extraction (need prose extraction; separate deferred work).
- Persisting the relationship onto entities (a fill-based approach is possible later; query-time keeps this slice migration-free).

## Decisions

**D1 — Query-time join, not a data fill.** A `SpellClassIndex` loads `sources.json` once (cached singleton) and answers `CanCast(className, spellName, source)`. The join runs at query time; nothing about the entity store or ingestion changes. Rationale: the relationship already exists complete on disk; filling it into entities would require a re-fill + re-ingest (a live migration) for no query-time benefit over a cached in-memory index of ~900 entries.

**D2 — `SpellClassIndex` shape.** Parse `sources.json` → for each `(source, spellName)` collect `class[].name` (and `classVariant[].name` if present). Build `Dictionary<(normName, normSource), HashSet<string>>` (classes per spell) — normalize names lower-case, alphanumeric-only, so our entity `Name`/`SourceBook` match the 5etools keys robustly. Source normalization maps our book keys/display to 5etools source codes where they differ (best-effort; unmatched source falls back to name-only membership across sources). Expose `ClassesFor(name, source)` and `CanCast(class, name, source)`.

**D3 — Completeness via scroll-all-then-filter.** For `castableByClass`, the service forces `type=Spell`, calls `ListByFilterAsync(filters, cap: MaxScan)` (MaxScan comfortably ≥ all ~700 spells) to get the full payload-filtered spell set, then filters in-memory with `SpellClassIndex.CanCast`. **Total = the class-filtered count** (not the pre-filter count); rows = the first `limit` compact `EntitySetRow`s. This preserves `entity-set-query`'s honest total-vs-returned/truncation contract — the count reflects the class filter, never the raw type count.

**D4 — Surface.** `EntitySearchQuery` gains `CastableByClass` (string, nullable). `list_entities` and `GET /retrieval/entities/list` gain a `castableByClass` param; `.http`/`.insomnia` updated. The router is unchanged — `list_entities` is already in the `structured-lookup` group.

## Risks / Trade-offs

- **Name/source matching misses** (our entity name vs the 5etools key) → Mitigation: alphanumeric-lowercase normalization on both sides; source-agnostic fallback (match by name across sources) so a book-key mismatch degrades to a slightly looser (still class-correct) match rather than a miss.
- **MaxScan bound** — if a filter somehow matches more spells than MaxScan, the join under-counts. Mitigation: spells are bounded (~700 corpus-wide); MaxScan set well above (e.g. 3000) and logged if ever hit.
- **Subclass-only spells** — a spell granted only via a subclass isn't returned by a class-level query. Accepted for this slice (documented); the index retains subclass data for a later refinement.
- **`sources.json` absence** (non-standard 5etools dir) → Mitigation: the index loads empty and `CanCast` returns false; `castableByClass` then yields an empty set (honest), never an error.
