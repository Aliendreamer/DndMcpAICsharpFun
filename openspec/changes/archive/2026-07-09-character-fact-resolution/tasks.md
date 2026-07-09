## 1. Richer canonical model (Table + ChoiceSet)

- [ ] 1.1 Add domain types: `ProvenanceRef { BlockId, SourceBook, Page }`, `CanonicalTable { Name, Columns[], Rows[ Cells[ Value, Provenance ] ] }`, `CanonicalChoiceSet { Id, Name, Options[ { Key, RowRef, Provenance } ] }` (TDD: a serialization round-trip test)
- [ ] 1.2 Extend the canonical JSON model/loader so a book file can carry `Tables` and `ChoiceSets` alongside `Entities`; validate ids/provenance on load (TDD)

## 2. Author the Dragonborn fixtures

- [ ] 2.1 Author `Draconic Ancestry` table (one row per ancestry: damageType, breathArea, saveAbility) with per-cell provenance to the real PHB chunk(s); author the `breath-damage-by-tier` table (L1–5 1d10 … L16–20 4d6)
- [ ] 2.2 Author the `draconic-ancestry` choice-set (options reference ancestry-table rows); commit as a canonical fixture (e.g. extend `phb14.json` or a slice fixture)
- [ ] 2.3 Validate the authored fixture loads + passes id/provenance validation

## 3. Postgres structured-fact tables (persistence)

- [ ] 3.1 Add EF entities `StructuredEntity`, `StructuredTable`, `StructuredTableRow`, `ChoiceSet` (+ provenance columns) and `DbSet`s + `AppDbContext` configuration
- [ ] 3.2 Add an EF migration for the new tables; confirm it applies cleanly (MigrateDatabaseAsync)
- [ ] 3.3 Persistence test (Testcontainers/Respawn): the tables round-trip a table + choice-set with provenance

## 4. Projection canonical → Postgres

- [ ] 4.1 `StructuredFactProjector`: read authored `Tables`/`ChoiceSets` from canonical JSON → upsert into the Postgres tables, idempotently (TDD on the upsert/idempotency)
- [ ] 4.2 Expose the projection trigger (admin endpoint `POST /admin/books/{id}/project-structured` OR fold into `ingest-entities`); if a new HTTP route, update `DndMcpAICsharpFun.http` + `dnd-mcp-api.insomnia.json` in the same commit
- [ ] 4.3 Persistence test: project the Dragonborn fixture, then query `ancestry='Red'` returns exactly one row with type/area/save + provenance

## 5. CharacterSheet resolved-choices slot

- [ ] 5.1 Add `ResolvedChoices` (`Dictionary<string,string>`) to `CharacterSheet`; serialization defaults to empty when absent
- [ ] 5.2 Back-compat test: a pre-change `HeroSnapshot` JSON (no `ResolvedChoices`) deserializes with an empty map (no migration error)
- [ ] 5.3 Set a Dragonborn hero's `ResolvedChoices["ancestry"] = "phb14.choiceset.draconic-ancestry:Red"` in a test fixture/seed path

## 6. CharacterResolutionService (engine)

- [ ] 6.1 `BreathWeaponTierMap` (level → dice) + `proficiencyBonus(level)` + `saveDC = 8 + prof + ConMod` as pure, unit-tested helpers (TDD: L3→1d10, L11→3d6, L17→4d6; DC at given Con/level)
- [ ] 6.2 `CharacterResolutionService.resolve(heroId, "breath weapon")`: load `HeroSnapshot` → read `ResolvedChoices.ancestry` → fetch ancestry row + tier dice from Postgres → compose `{ value, components[], provenance[], confidence }` (TDD with a fake/seeded store)
- [ ] 6.3 `needsReview` component returns its prose span (fallback) instead of a computed value (TDD)
- [ ] 6.4 Register the service in DI

## 7. End-to-end integration (real Postgres)

- [ ] 7.1 Integration test (Testcontainers): seed the projected Dragonborn tables + a Red Dragonborn hero at L3 / L11 / L17 → `resolve` returns the correct cited breath weapon (fire, 15-ft cone, Dex, DC, dice) with 3 provenance refs

## 8. MCP tool (read)

- [ ] 8.1 Add `resolve_character_feature(heroId, feature)` to the MCP server tool surface; returns the resolution result
- [ ] 8.2 Test the tool returns the structured result for the Red Dragonborn; confirm the chat client can invoke it
- [ ] 8.3 Update `DndMcpAICsharpFun.http` + `dnd-mcp-api.insomnia.json` if any new HTTP surface was added; `dotnet build` 0 warnings + full non-persistence suite green
