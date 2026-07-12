## 1. Allowlist config + field-merge core

- [ ] 1.1 Add a declarative per-type structured-field allowlist (`type → {field names}`) — seed Class (`hd`, `classFeatures`, `subclassTitle`, `proficiency`, `spellcasting`, `classTableGroups`), Subclass (`subclassFeatures`), Spell (`level`, `school`, `range`, `components`, `duration`, `classes`), Monster (extraction-thin fields e.g. `environment`, `traitTags`). Structured mechanics only — never `entries`.
- [ ] 1.2 Add a pure field-merge function: given an extraction entity's `Fields`, its `fivetoolsFilledFields`, and the matched 5etools record's `Fields`, apply the merge rules (absent→fill+record; present&listed→re-derive; present&not-listed→untouched) and return the merged `Fields` + updated `fivetoolsFilledFields`. Never touches `entries`.
- [ ] 1.3 Unit-test the merge function: absent fills; extraction-produced field untouched; re-run byte-identical (idempotent); a field not in the allowlist is never added.
- [ ] 1.4 Build 0/0; suite green.

## 2. EntityFieldFillService (per-book, name-matched, canonical write)

- [ ] 2.1 Add `Features/Ingestion/FivetoolsIngestion/EntityFieldFillService.cs` — for an `IngestionRecord` with a `FivetoolsSourceKey`: load the canonical (`CanonicalJsonLoader`); for each entity of a type that has an allowlist, name-match to its 5etools record via `EntityNameIndex.Normalize` + the book's edition (reuse the roster-backfill machinery — `FivetoolsSourceRegistry` for the roster, `FivetoolsMapperRegistry`/`BuildFields` for the 5etools `Fields`); apply the merge (Task 1); skip `dataSource:"manual"` entities. No source key → no-op result.
- [ ] 2.2 Write the updated canonical atomically (temp + rename) via `CanonicalJsonWriter`, only when something changed; keep `dataSource` = extraction.
- [ ] 2.3 Test with a fake canonical + fake 5etools record (mirror the roster-backfill tests): a prose Class gains `hd`/`classFeatures`, `entries` untouched, `fivetoolsFilledFields` recorded, `manual` entity skipped; a no-source-key book is a no-op.
- [ ] 2.4 **Canonical-rewrite gates:** assert unique-id invariant + reload the written file with `CanonicalJsonLoader`. **Real-data spot-check:** a test that reads the real `5etools/class/*.json` and asserts they carry the allowlisted fields (`hd`/`classFeatures`/`subclassTitle`) — don't trust a self-seeded fixture.
- [ ] 2.5 Register the service in the correct `Add*` group (+ the scope-test replica if covered). Build 0/0; **full** suite green.

## 3. `fill-fields` admin endpoint

- [ ] 3.1 Add `POST /admin/books/{id}/fill-fields` (admin-key guarded, `DisableAntiforgery`) that runs `EntityFieldFillService` on the book's canonical and returns a report (entities touched, fields filled per type). No source key → success with a no-op report.
- [ ] 3.2 Update `DndMcpAICsharpFun.http` AND `dnd-mcp-api.insomnia.json` with the new route (admin-key header) in the same commit.
- [ ] 3.3 Build 0/0; full suite green.

## 4. Auto-run in the extract pipeline

- [ ] 4.1 Wire the field-fill to run automatically when `extract-entities` completes for a book with a source key (merges into the canonical extraction just wrote). Behavior-neutral for a no-source-key book (no-op).
- [ ] 4.2 Test: after extract completes for an official book, the canonical carries the allowlisted fields; a forced re-extract re-derives the same result (durability).
- [ ] 4.3 Build 0/0; full suite green.

## 5. One-time cleanup — undo the wholesale ImportAll

- [ ] 5.1 Run `fill-fields` on the core books (`phb14`, `mm14`, `dmg14`, and any other extracted official books) so their canonical classes gain structured fields. (Operational step against the live stack, not a code change.)
- [ ] 5.2 Rebuild `dnd_entities` from the canonical — a `Tools/` console or direct Qdrant op that clears the collection, then `ingest-entities` per book — dropping the `dataSource:"5etools"` strays and restoring extraction entities (`dataSource` back to extraction) + the newly field-filled classes. Not a permanent HTTP route.
- [ ] 5.3 **Live verify:** the level-up card grounds all 12 classes from **extraction** entities (`dataSource:"llm"` + `fivetoolsFilledFields`), and a monster (e.g. Aboleth) reads back as the extraction version — hybrid restored end-to-end.
