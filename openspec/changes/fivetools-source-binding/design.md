## Context

The project ingests D&D content from two sources: 5etools JSON files (direct, structured) and PDFs (via Docling + LLM extraction). 5etools identifies every book with a short source key (`PHB`, `XDMG`, etc.) and entities carry `source: "PHB"` in their JSON. Our PDF-based pipeline has no equivalent — `IngestionRecord` stores a display name and a coarse `BookType` enum, and extracted entities end up with `sourceBook` set to the display name or empty. This blocks unified search, edition filtering, and MCP intent resolution across both pipelines.

`5etools/books.json` already contains the full source registry with `id`, `name`, `group` (core/supplement/setting/…), and `published` date. It is read-only data we can use as a lookup table.

## Goals / Non-Goals

**Goals:**
- Bind each `IngestionRecord` to an optional 5etools source key
- Derive edition, group, display abbreviation, and year from that key at runtime
- Normalize `sourceBook` in Qdrant across both pipelines to the source key
- Add `srd`, `srd52`, `basicRules2024` flags to entity payloads
- Allow MCP tools to resolve intent aliases ("core books", "2024", "srd") to source key lists

**Non-Goals:**
- Modifying any file under `5etools/`
- Automatic PDF→source-key detection (matching is manual at registration, with suggestions)
- Full SRD flag population for extracted entities (defaults to `false`; hand-correctable)
- Renaming or migrating existing `BookType` enum values

## Decisions

### D1: FivetoolsSourceKey is nullable, not required

Books not in 5etools (homebrew, third-party) should still be ingestable. When `FivetoolsSourceKey` is null, edition falls back to `BookType` (Core → Edition2014/Edition2024 must be supplied via a separate `editionYear` field at registration, TBD). For now, null means unknown edition.

**Alternative considered:** require a source key for all books. Rejected — too restrictive for custom content.

### D2: BookSourceRegistry reads books.json once at startup

The registry is a singleton loaded once from disk. books.json is ~60 entries and changes only when we pull a new 5etools data drop. No database needed.

**Alternative considered:** store source metadata in SQLite. Rejected — adds migration complexity for data that's already on disk and changes rarely.

### D3: Display abbreviation is derived, not stored

`DMG'24` is computed as: strip leading `X` from source key (if present), append `'YY` from the last two digits of the published year. `XDMG` (2024) → `DMG'24`. `PHB` (2014) → `PHB'14`. This avoids a separate display field and stays consistent with the 5etools website convention.

**Edge cases:** source keys that don't start with X (e.g. `TCE` 2020 → `TCE'20`). No stripping needed; just append year.

### D4: SRD flags go on entity payload, not on IngestionRecord

SRD availability is per-entity (one spell might be SRD, another from the same book is not). Storing it on `IngestionRecord` would be wrong — it belongs on each entity in canonical JSON and Qdrant payload.

### D5: Suggestion-only fuzzy matching at registration

When `fivetoolsSourceKey` is not provided in the registration form, the API returns the top-3 fuzzy name matches from books.json in the response (not auto-applied). The user must explicitly pass the key to bind it. This prevents silent mismatches (DMG vs XDMG have nearly identical names).

## Risks / Trade-offs

- **DMG vs XDMG name collision** → Fuzzy match will surface both; the user must pick. The `published` year shown in `GET /admin/5etools/sources` helps distinguish.
- **Stale books.json** → If we pull a new 5etools data drop that renames a source key, existing `FivetoolsSourceKey` values in SQLite may point to a gone key. Mitigation: `BookSourceRegistry` logs a warning on startup for any `IngestionRecord` whose key isn't in books.json.
- **Extracted entities with no source key** → If a book was registered before this feature, its canonical JSON entities have empty `source`. Mitigation: a one-time admin endpoint to re-extract or patch source fields, out of scope for this change.
- **SRD flags default false** → Extracted entities that are actually SRD won't be discoverable via `srd=true` filter until hand-corrected. Acceptable for now.

## Migration Plan

1. Add nullable `FivetoolsSourceKey` column via EF Core migration — additive, no data loss.
2. `BookSourceRegistry` registered in DI at startup — no API changes required for existing callers.
3. `GET /admin/5etools/sources` and updated `POST /admin/books/register` ship together.
4. Existing `IngestionRecord` rows have `FivetoolsSourceKey = null` — behaviour unchanged.
5. New Qdrant payload fields (`srd`, `srd52`, `basicRules2024`) added to collection index — additive.
6. Existing Qdrant points without these fields are simply unmatched by `srd=true` filters (safe default).

**Rollback:** Remove the migration and revert the registration endpoint. No data is lost since the column is nullable and new endpoints are additive.

## Open Questions

- Should `editionYear` be a separate registration field for non-5etools books, or should we infer it from `BookType`?
- Should `GET /admin/5etools/sources` be paginated or return all ~60 entries at once?
