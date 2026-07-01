## 1. SpellBackfillService

- [x] 1.1 Add `SpellBackfillService` (TDD): given a book record, resolve its `FivetoolsSourceKey` (via `BookSourceRegistry`); if none → no-op result. Load the canonical `<slug>.json` Spell entities; collect normalized names. From 5etools spells filtered to `source == sourceKey` (reuse `FivetoolsRecordIndex`/the spell loader), build a Spell entity for each whose normalized name is absent, and return the list to append. Map: envelope (`EntityIdSlug.For(bookKey, EntityType.Spell, name)`, name, sourceBook/edition from registry, page/firstAppearedIn from the 5etools record) + `fields.entries=[{type:entries,name:Description,entries:<5etools.entries>}]`, `entriesHigherLevel`, `damageInflict`/`conditionInflict`; `dataSource="5etools-backfill"`, `disposition=Accepted`, `needsReview=false`. TDD: maps a full spell; skips present; homebrew no-op; XPHB filtered out.

## 2. Endpoint + wiring

- [x] 2.1 `POST /admin/books/{id}/backfill-spells` → calls the service, appends the backfilled entities to the canonical (reuse the extraction's canonical writer), returns `{backfilled:[names], alreadyPresent:N}`. Register the endpoint; `[FromServices]` handler. TDD via the endpoint test host.
- [x] 2.2 Update `DndMcpAICsharpFun.http` AND `dnd-mcp-api.insomnia.json` with the new endpoint (same commit).

## 3. Build

- [x] 3.1 `dotnet build` 0 warnings; full non-persistence suite green.

## 4. Live validation

- [x] 4.1 `POST /admin/books/2/backfill-spells` on PHB. Confirm 6 backfilled (Contact Other Plane, Hallucinatory Terrain, Prismatic Wall, Produce Flame, Ray of Sickness, Regenerate), ~355 already present, **PHB spells = 361/361**, `POST /admin/canonical/validate` has no new FAIL/dup-id, parsed entities untouched. (Re-add Gnome if a prior force run wiped it.)
