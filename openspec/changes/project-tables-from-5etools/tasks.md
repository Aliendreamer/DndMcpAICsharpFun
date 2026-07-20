## 1. Captioned embedded table projector

- [ ] 1.1 Add a `CaptionedTableProjector` (in the app's ingestion/extraction namespace, reusable by the console) that recurses a 5etools entity object for `{type:table}` blocks with a `caption` and maps caption/`colLabels`/`rows` → `CanonicalTable`, id `<book-slug>.table.<EntityIdSlug(caption)>`, `dataSource:"5etools-table-projection"`, provenance `sourceBook=KEY` + entity page.
- [ ] 1.2 Unit-test: Dragonborn PHB → `phb14.table.draconic-ancestry` with exact columns/rows (per spec scenario); uncaptioned block skipped.

## 2. Class progression table synthesizer

- [ ] 2.1 Add a `ClassProgressionTableProjector`: martial path (no `classTableGroups`) synthesizes `Level | Proficiency Bonus | Features` over levels 1–20 (standard prof curve; features from `classFeatures` per level).
- [ ] 2.2 Caster path: append `classTableGroups` columns — strip `{@filter …}`/`{@tag …}` markup to plain labels, expand `rowsSpellProgression` into per-level slot cells, join on level; skip-and-log any unrecognized group encoding.
- [ ] 2.3 Unit-test martial (Fighter: 20 rows, level-5 prof `+3`) and caster (Wizard: plain slot labels, per-level slot counts) per spec scenarios.

## 3. ProjectTables console

- [ ] 3.1 Add `Tools/ProjectTables` console (reuses `CanonicalJsonLoader`, `BookCatalog`, both projectors; no DI/DB). Args: `<book-slug>` or `--all`.
- [ ] 3.2 Per book: resolve `fivetoolsSourceKey` via `BookCatalog`; skip + report books with none; for official books build the combined `tables[]` (captioned + progression) and REPLACE the canonical `tables[]` wholesale.
- [ ] 3.3 Enforce unique ids (numeric-suffix collisions); write the canonical; reload via `CanonicalJsonLoader` and fail loudly on error.
- [ ] 3.4 Unit-test the console orchestration on a fixture book: official → replaced + all `dataSource:"5etools-table-projection"`; homebrew (no key) → skipped, `tables[]` unchanged; unique-id + round-trip.

## 4. Live validation & corpus run

- [ ] 4.1 Run `ProjectTables phb14`; inspect `phb14.json` `tables[]` — confirm `phb14.table.draconic-ancestry` (cols/rows), a synthesized `phb14.table.fighter` and `phb14.table.wizard`; assert unique ids + reload.
- [ ] 4.2 `ingest-entities` on PHB; exercise the real resolution path (breath-weapon resolve / `GET /retrieval/entities/...`) and confirm it resolves against the projected `phb14.table.draconic-ancestry`.
- [ ] 4.3 Run `ProjectTables --all`; review the `tables[]` diff for each official book (PHB, DMG, XGE, MPMM, MTF, ERLW, SCAG, MM, TCE); confirm no MinerU-origin tables remain and each canonical reloads.

## 5. Gates

- [ ] 5.1 `dotnet build` 0/0; `dotnet test` full suite green; `dotnet format --verify-no-changes`.
- [ ] 5.2 No HTTP/endpoint change (console only) — confirm `.http`/insomnia untouched; no security-surface change to review.

## 6. Resolution wiring (live-validation scope expansion)

- [ ] 6.1 `DraconicAncestryResolutionProjector`: from the 5etools PHB Dragonborn race, emit normalized `phb14.table.draconic-ancestry` (ancestry/damageType/breathArea/saveAbility; parse "<area> (<Abbr>. save)"), `phb14.choiceset.draconic-ancestry` (per-ancestry → rowIndex), `phb14.table.breath-damage-by-tier` (tier/dice from BreathWeaponRules). Unit-tested.
- [ ] 6.2 Console wiring: generic projection cedes resolution-owned table ids; `ProjectTablesRunner` composes generic(−owned)+resolution tables and writes resolution choiceSets (`file with { Tables=…, ChoiceSets=… }`). Tests updated (PHB gets normalized draconic + choiceset + tier; non-PHB unaffected).
- [ ] 6.3 Real-Postgres (Testcontainers) integration test: project PHB artifacts → `StructuredFactProjector.ProjectAsync` → resolve a L5 Black Dragonborn breath weapon → assert "5 by 30 ft. line of acid, Dexterity save DC N, 1d10", confidence "ok". Mirror `CharacterResolutionIntegrationTests`.
- [ ] 6.4 Real-corpus check: run console on PHB; confirm `phb14.json` contains the normalized draconic table + choiceset + tier table and reloads.
