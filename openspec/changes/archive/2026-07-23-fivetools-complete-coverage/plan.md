# fivetools-complete-coverage Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development. Steps use `- [ ]`.

**Goal:** Backfill every modeled catalog type from 5etools (gap-only) and add a per-book coverage gate that names the gaps and warns (never blocks).

**Architecture:** New `IFivetoolsBackfillProvider` implementations mirroring `SpellBackfillProvider`/`GodBackfillProvider` (their own `EnumerateRoster` + curated `BuildEntity`), registered in the DI provider array so the existing `EntityBackfillService` engine + backfill endpoint cover them. A `FivetoolsCoverageService` runs every provider's `ComputeAsync` without applying, aggregated into a report surfaced via a new admin endpoint + validate-warning + startup log.

**Tech Stack:** .NET 10, System.Text.Json, xunit + FluentAssertions + NSubstitute, Testcontainers where the engine touches disk/DB.

## Global Constraints

- **Serena for ALL tracked files** (reads/edits/creates/searches) — built-in Read/Edit/Write or bash grep/cat on a tracked file = task failure (revert + redo via Serena). Git-ignored `.superpowers/` scratch = built-in OK. `mcp__plugin_serena_serena__initial_instructions` first. Stop after one >2-min Serena hang and report.
- `dotnet` needs `dangerouslyDisableSandbox: true`; warnings-as-errors → build 0/0; LSP false CS0246/CS1061 on test files are noise (trust the build). Docker up for persistence/engine tests.
- **Gap-only, additive:** never modify/delete an extraction-owned or prior entity. Projected entities: `DataSource:"5etools-backfill"`, `Disposition.Accepted`, id/edition from the BOOK's source key.
- **Providers mirror `SpellBackfillProvider`/`GodBackfillProvider`, NOT the field-fill mappers** (the mapper's `Map` emits a different, field-fill shape — `DataSource:"5etools"`, raw-clone Fields, no Accepted). Curated `BuildFields` must match the type's canonical `*Fields` domain shape.
- **Coverage NEVER blocks** — report + warning only (endpoint 200, validate stays 200/valid, startup `LogWarning`).
- Endpoint change → update `DndMcpAICsharpFun.http` + `dnd-mcp-api.insomnia.json` same commit; admin-gate the new endpoint; security review on the endpoint diff.

---

### Task 1: Prose-catalog providers — Feat, Background, Condition

**Files:**
- Create: `Features/Ingestion/FivetoolsIngestion/Providers/FeatBackfillProvider.cs`, `BackgroundBackfillProvider.cs`, `ConditionBackfillProvider.cs`
- Test: `DndMcpAICsharpFun.Tests/Entities/Admin/CatalogBackfillProviderTests.cs`
- Reference (read via Serena): `Providers/SpellBackfillProvider.cs`, `Providers/GodBackfillProvider.cs` (the pattern), `IFivetoolsBackfillProvider.cs`, and the target `*Fields` domain types (`Domain/Entities/Fields/FeatFields`, `BackgroundFields`, `ConditionFields` — confirm exact names) + the field-fill mapper `BuildFields` overrides if any (`Mappers/FivetoolsFeatMapper` etc.).

**Interfaces:**
- Produces: three `IFivetoolsBackfillProvider` (`Type` = Feat/Background/Condition). `EnumerateRoster(fivetoolsDir)` → all elements of that type across sources (Feat→`feats.json` `"feat"`; Background→`backgrounds.json` `"background"`; Condition→`conditionsdiseases.json` `"condition"` — confirm array keys by reading the files' shape). `BuildEntity(sourceKey, edition, name, element)` → curated canonical entity.

- [ ] Step 1: Write failing tests — for each provider: (a) `BuildEntity` returns `Type`, `DataSource=="5etools-backfill"`, `Disposition==Accepted`, `Id==EntityIdSlug.For(key,Type,name)`, non-empty `Fields` matching the `*Fields` shape (a description/entries block present); (b) `EnumerateRoster` over a tiny temp 5etools dir yields the seeded elements. Mirror `SpellBackfillProvider`-style assertions if an existing provider test exists (search first).
- [ ] Step 2: Run tests → FAIL (types don't exist).
- [ ] Step 3: Implement the three providers mirroring `GodBackfillProvider` (prose types: wrap `entries` into a `{type:entries,name:Description,entries:[...]}` block via `FivetoolsEntryText`/the Spell pattern; copy the type's few structured props into `*Fields`).
- [ ] Step 4: Run tests → PASS. `dotnet build` 0/0.
- [ ] Step 5: Commit `feat(backfill): Feat/Background/Condition backfill providers (5etools coverage)`.

### Task 2: Prose-catalog providers — Trap, DiseasePoison, VehicleMount

**Files:**
- Create: `Providers/TrapBackfillProvider.cs`, `DiseasePoisonBackfillProvider.cs`, `VehicleBackfillProvider.cs`
- Test: extend `CatalogBackfillProviderTests.cs`
- Reference: same as Task 1 + the existing `FivetoolsTrapMapper`/`DiseasePoisonMapper`/`VehicleMapper` + `*Fields` types; confirm the 5etools files (`trapshazards.json`, `conditionsdiseases.json` disease/poison arrays, `vehicles.json`).

**Interfaces:**
- Produces: three `IFivetoolsBackfillProvider` (Trap/DiseasePoison/VehicleMount).

- [ ] Step 1: Failing tests as Task 1 for the three types (roster over temp dir + BuildEntity shape).
- [ ] Step 2: Run → FAIL.
- [ ] Step 3: Implement mirroring Task 1. Confirm the DiseasePoison array key(s) — it may span "disease"/"poison"; enumerate both if so.
- [ ] Step 4: Tests PASS; build 0/0.
- [ ] Step 5: Commit `feat(backfill): Trap/DiseasePoison/Vehicle backfill providers (5etools coverage)`.

### Task 3: Base-item providers — Item, Weapon, Armor (the partition set, design D2)

**Files:**
- Create: `Providers/ItemBackfillProvider.cs`, `WeaponBackfillProvider.cs`, `ArmorBackfillProvider.cs`
- Test: `DndMcpAICsharpFun.Tests/Entities/Admin/BaseItemBackfillProviderTests.cs`
- Reference: `Mappers/FivetoolsItemMapper.cs`, `FivetoolsWeaponMapper.cs`, `FivetoolsArmorMapper.cs` (how they classify a base item by type code), `MagicItemBackfillProvider.cs` (reads `items.json` filtering rarity-present — the mundane providers must NOT double-count magic items), the `*Fields` types (`WeaponFields`/`ArmorFields`/`ItemFields`).

**Interfaces:**
- Produces: three `IFivetoolsBackfillProvider` (Item/Weapon/Armor). Roster from `items-base.json` (`"baseitem"`) + mundane (rarity-absent) `items.json` `"item"`, partitioned by 5etools type code: Weapon = melee/ranged weapon codes; Armor = LA/MA/HA/shield codes; Item = everything else mundane. Each element claimed by exactly ONE provider.

- [ ] Step 1: Failing tests — seed a temp 5etools dir with a known base-item subset (a weapon, an armor, a mundane gear item, and a magic item with rarity); assert Weapon.EnumerateRoster yields only the weapon, Armor only the armor, Item only the mundane gear, and NONE yield the magic item (that's MagicItem's); assert BuildEntity shapes (`WeaponFields` has damage/properties, `ArmorFields` has AC, etc.).
- [ ] Step 2: Run → FAIL.
- [ ] Step 3: Implement the three, sharing a private type-code classifier so the partition is defined once. Cross-check the code set against the three mappers.
- [ ] Step 4: Tests PASS (esp. the no-overlap/no-drop partition + magic-item exclusion); build 0/0.
- [ ] Step 5: Commit `feat(backfill): mundane Item/Weapon/Armor providers with base-item partition (5etools coverage)`.

### Task 4: Register providers + multi-type apply round-trip

**Files:**
- Modify: `Extensions/ServiceCollectionExtensions.cs` (the `IFivetoolsBackfillProvider[]` array in `AddEntityExtraction`, ~line 284) — add all 9.
- Modify: `DndMcpAICsharpFun.Tests/Entities/Admin/EntityBackfillEndpointTests.cs` (`BuildClientAsync` provider-array replica) — add all 9.
- Test: add to `EntityBackfillEndpointTests` (or a new test) a multi-type apply asserting the canonical round-trips.

**Interfaces:**
- Consumes: the 9 providers from Tasks 1-3.

- [ ] Step 1: Failing test — seed a book canonical + a temp 5etools dir with gaps across several new types; run the backfill apply for the book; reload the written canonical via `CanonicalJsonLoader`; assert (a) it loads with NO duplicate-id error, (b) `ids.Should().OnlyHaveUniqueItems()`, (c) an extraction-owned entity in the seed is byte-unchanged, (d) the new entities have `DataSource=="5etools-backfill"`.
- [ ] Step 2: Run → FAIL (providers not registered → gaps not filled).
- [ ] Step 3: Add the 9 providers to BOTH the DI array and the test replica.
- [ ] Step 4: Test PASS; FULL `dotnet test` green (registration ripples through DI-graph tests); build 0/0.
- [ ] Step 5: Commit `feat(backfill): register 9 catalog providers + multi-type apply round-trip (5etools coverage)`.

### Task 5: Coverage service

**Files:**
- Create: `Features/Ingestion/FivetoolsIngestion/FivetoolsCoverageService.cs`, `Domain/.../BookCoverage.cs` (record types: `BookCoverage`, `TypeCoverage`)
- Test: `DndMcpAICsharpFun.Tests/Entities/Admin/FivetoolsCoverageServiceTests.cs`
- Reference: `EntityBackfillService.ComputeAsync` (returns the per-type numbers + `missing[]` NAMED without applying), `EntityBackfillServiceTests` `FakeProvider` pattern.

**Interfaces:**
- Produces: `FivetoolsCoverageService.ComputeAsync(IngestionRecord, ct) → BookCoverage { SourceKey, PerType: TypeCoverage[]{Type, RosterCount, Present, MissingCount, MissingNames[]}, Unmodeled: string[], TotalPresent, TotalRoster, CoveragePct }`. Non-official (no source key) → empty.

- [ ] Step 1: Failing tests — with fake providers over a temp canonical + roster: assert per-type roster/present/missing counts + `MissingNames`; assert `Unmodeled` contains a known-unmodeled 5etools type present for the book (e.g. `optionalfeatures`); assert non-official record → empty; assert `CoveragePct` math.
- [ ] Step 2: Run → FAIL.
- [ ] Step 3: Implement — loop all injected `IFivetoolsBackfillProvider`, call `EntityBackfillService.ComputeAsync` per provider (construct/parameterize per provider — mirror how the endpoint drives per-provider services), aggregate. `Unmodeled` from a small commented static list of 5etools files/keys with no `EntityType` (incl. `optionalfeatures`), reported only when the book's source actually has entries there. Register in DI.
- [ ] Step 4: Tests PASS; build 0/0.
- [ ] Step 5: Commit `feat(coverage): FivetoolsCoverageService — per-book/type gap report (5etools coverage)`.

### Task 6: Coverage surfaces — endpoint, validate-warning, startup log

**Files:**
- Modify: the admin books endpoints file (where `/admin/books/{id}/...` live — locate via Serena) — add `GET /admin/books/{id}/coverage`.
- Modify: `POST /admin/canonical/validate` handler/service — add a `coverage` warnings summary (stays 200/valid).
- Modify/Create: a startup coverage guard mirroring `ScopeHealthCheck` (one `LogWarning` per official book < 100%).
- Modify: `DndMcpAICsharpFun.http` + `dnd-mcp-api.insomnia.json`.
- Test: endpoint test (mirror `EntityBackfillEndpointTests`); validate-still-200 test; startup warn-below/silent-at-100 test.

**Interfaces:**
- Consumes: `FivetoolsCoverageService` (Task 5).

- [ ] Step 1: Failing tests — (a) `GET /admin/books/{id}/coverage` returns the `BookCoverage` JSON and is admin-gated (401 without key); (b) `validate` returns 200 with a coverage warning block for a below-100 book; (c) startup guard logs a warning below 100 and is silent at 100.
- [ ] Step 2: Run → FAIL.
- [ ] Step 3: Implement the three surfaces (all non-blocking). Update `.http` + insomnia with the new endpoint (admin header).
- [ ] Step 4: Tests PASS; FULL `dotnet test` green; build 0/0; `dotnet format` clean.
- [ ] Step 5: Commit `feat(coverage): coverage endpoint + validate warning + startup guard (5etools coverage)`.

### Task 7: Live PHB validation (dev-flow data gate — do NOT skip)

**Files:** none (operational); notes → `.superpowers/sdd/coverage-validation.md`.

- [ ] Step 1: Run backfill on PHB (`POST /admin/books/{id}/backfill-entities` or the actual route — confirm from `.http`). Review the canonical git diff: feats 1→42, backgrounds→20, base gear, conditions — spot-check names are real 5etools entities, `dataSource:"5etools-backfill"`.
- [ ] Step 2: Confirm GAP-ONLY — pick an extraction-owned PHB entity (e.g. `phb14.spell.fireball`) and verify it is byte-unchanged in the diff; confirm unique ids (the written file reloads).
- [ ] Step 3: `GET /admin/books/{id}/coverage` → provided types read ~100%; the `unmodeled` bucket shows the `optionalfeatures` gap (the ~82). Record the numbers.
- [ ] Step 4: `ingest-entities` PHB → the new entities land in `dnd_entities`; a spot `GET /retrieval/entities/search` for a backfilled feat returns it.
- [ ] Step 5: Commit any canonical changes `data(phb): 5etools catalog backfill (feats/backgrounds/gear/conditions)`.

### Task 8: Gates + whole-branch review

- [ ] Step 1: `dotnet build` 0/0; FULL `dotnet test` green; `dotnet format --verify-no-changes`; `.http`+insomnia synced; security/admin-gate review on the `/coverage` endpoint diff.
- [ ] Step 2: Whole-branch review (most-capable model) over the full change range; fix Critical/Important in one pass.
