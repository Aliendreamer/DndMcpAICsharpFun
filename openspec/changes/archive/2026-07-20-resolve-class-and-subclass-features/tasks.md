## 1. SubclassSpellsProjector

- [ ] 1.1 Add `SubclassSpellsProjector.Project(fivetoolsDir, sourceKey) : IReadOnlyList<CanonicalTable>` in `Features/Ingestion/FivetoolsIngestion/`: for each `subclass` entry with `source==sourceKey` across `5etools/class/class-*.json`, read `additionalSpells`, union `prepared`/`known`/`expanded` per level, emit `<slug>.table.<subclass-slug>-spells` (cols `["level","spells"]`, comma-joined). Slug via `EntityIdSlug`. Skip subclasses with no/unhandled `additionalSpells`. Provenance `ProvenanceRef("<slug>.5etools", sourceKey, page)`.
- [ ] 1.2 Unit-test: Life Domain (`prepared`) → `phb14.table.life-domain-spells` L1 = bless/cure wounds; a Warlock patron (`expanded`) → expanded spells present; a martial subclass → no table.

## 2. Console wiring

- [ ] 2.1 In `ProjectTablesRunner.RunOneAsync`, compose the subclass-spells tables into the projected set alongside the generic + draconic-resolution tables (same cede/unique-id discipline; subclass-spells ids don't collide with class tables). Keep the empty-projection skip guard.
- [ ] 2.2 Update `ProjectTablesConsoleTests` (PHB fixture gains a subclass with `additionalSpells`) → assert a `*-spells` table appears; homebrew/empty still skipped.

## 3. Class-features resolver

- [ ] 3.1 Add `ResolveClassFeaturesAsync(sheet, ct)` in `CharacterResolutionService`: per `sheet.Classes[]`, load `phb14.table.<class-slug>` from `StructuredTables`, read rows `level ≤ ClassLevel`, split `Features` cells (comma) into the cumulative list, take current-level `Proficiency Bonus`; one `ResolvedComponent` per class; absent table → `needsReview`. Wire `"class features"` into `ResolveForSheetAsync`.
- [ ] 3.2 Unit-test (fake/seeded table): L6 Fighter → cumulative features incl. Extra Attack + ASI, prof `+3`; a class with no table → `needsReview`.

## 4. Subclass-spells resolver

- [ ] 4.1 Add `ResolveSubclassSpellsAsync(sheet, ct)` in `CharacterResolutionService`: slug `sheet.Subclass` → query `StructuredTables` for `CanonicalId` ending `.table.<subclass-slug>-spells`, collect rows `level ≤ sheet.Level`, return the granted spells cited; absent → `needsReview`; present-but-empty → value "none", confidence `ok`. Wire `"subclass spells"` into `ResolveForSheetAsync`.
- [ ] 4.2 Unit-test (seeded table): L5 Life Cleric → domain spells through L5; absent subclass → `needsReview`.

## 5. Tool surface

- [ ] 5.1 Add `"class features"` and `"subclass spells"` to the `resolve_character_feature` tool description in `DndChatService`. Add both to the router/QueryRouterOptions examples if that's where feature phrases live.

## 6. Live validation & corpus

- [ ] 6.1 Real-Postgres (Testcontainers) integration test: project real PHB (class tables already present + new subclass-spells) → `StructuredFactProjector` → resolve a L6 Fighter `"class features"` (Extra Attack/ASI, prof +3, confidence ok) AND a L5 Life Cleric `"subclass spells"` (bless…revivify, confidence ok). Mirror `ProjectedDraconicResolutionIntegrationTests`.
- [ ] 6.2 `ProjectTables --all`; confirm the new `*-spells` tables appear in projected canonicals, all reload, unique ids, class tables unchanged; review the diff.

## 7. Gates

- [ ] 7.1 `dotnet build` 0/0; FULL `dotnet test` green; `dotnet format` clean on new files.
- [ ] 7.2 No HTTP endpoint change (console + in-process resolver + existing tool) → `.http`/insomnia untouched; the new per-user resolver features go through the existing `ResolveForUserAsync` ownership path (no new spoofable-id surface).
