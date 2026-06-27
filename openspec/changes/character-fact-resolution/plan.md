# character-fact-resolution (slice 1) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax.
>
> **CRITICAL — Serena:** every implementer/reviewer MUST call `mcp__serena__initial_instructions` first and use Serena symbolic tools for ALL `.cs` reads/edits (`find_symbol`, `replace_content`, `create_text_file`, `search_for_pattern`). Built-in Read/Edit on `.cs` is forbidden. Grep-verify after every edit. Bash only for `dotnet`/`git`.

**Goal:** Build the thinnest meaningful slice of a deterministic character-fact resolution engine — answer a Red Dragonborn's breath weapon by *computing + citing* from structured facts + character state, end-to-end (canonical Table model → Postgres projection → sheet slot → engine → MCP tool).

**Architecture:** First-class `CanonicalTable`/`CanonicalChoiceSet` shapes (with `ProvenanceRef`) authored by hand → projected into Postgres (`StructuredTable`/`StructuredTableRow`/`ChoiceSet`) → a deterministic `CharacterResolutionService` joins the resolved ancestry choice (new `CharacterSheet.ResolvedChoices`) through the tables + applies tier/DC rules → exposed as the MCP tool `resolve_character_feature`.

**Tech Stack:** C# / .NET 10, EF Core + Npgsql (Postgres), xUnit + FluentAssertions, Testcontainers+Respawn for persistence tests, ModelContextProtocol (MCP) `[McpServerTool]`.

## Global Constraints

- .NET 10, nullable, implicit usings, **warnings-as-errors** (0 warnings).
- TDD: failing test → minimal impl → green → commit. Commits are per-task on a feature branch; pause for the user's go-ahead before each `git commit` step.
- Edits via Serena; grep-verify after each.
- Persistence tests use the existing Testcontainers/Respawn harness (Docker must be up); non-persistence tests need no DB.
- EF config style: inline lambdas in `AppDbContext.OnModelCreating`; DbSets via `Set<T>()`. Repos use `IDbContextFactory<AppDbContext>` + `await using` short-lived contexts.
- Canonical loader options: `new JsonSerializerOptions(JsonSerializerDefaults.Web){ Converters = { new JsonStringEnumConverter() } }`.
- `CharacterSheet` serializes with **default** `System.Text.Json` options (PascalCase) via the EF value converter — a new property is auto-included; back-compat requires the new field tolerate absence (null → empty).
- HTTP-contract rule: any new `MapPost`/route → update `DndMcpAICsharpFun.http` AND `dnd-mcp-api.insomnia.json` in the same commit.
- Namespaces follow folder (e.g. `DndMcpAICsharpFun.Domain.Entities`, `DndMcpAICsharpFun.Features.Resolution`).

---

### Task 1: Canonical Table / ChoiceSet / ProvenanceRef domain types

**Files:**
- Create: `Domain/Entities/CanonicalKnowledge.cs`
- Test: `DndMcpAICsharpFun.Tests/Entities/CanonicalKnowledgeTests.cs`

**Produces:** records `ProvenanceRef(string BlockId, string SourceBook, int? Page)`, `CanonicalCell(string Value, ProvenanceRef? Provenance)`, `CanonicalTableRow(IReadOnlyList<CanonicalCell> Cells)`, `CanonicalTable(string Id, string Name, IReadOnlyList<string> Columns, IReadOnlyList<CanonicalTableRow> Rows)`, `CanonicalChoiceSet(string Id, string Name, IReadOnlyList<CanonicalChoiceOption> Options)`, `CanonicalChoiceOption(string Key, string TableId, int RowIndex, ProvenanceRef? Provenance)`.

- [ ] **Step 1: Failing test** — round-trip a `CanonicalTable` (2 columns, 1 row with provenance) through `System.Text.Json` with the loader options and assert columns/cells/provenance survive; assert `CanonicalChoiceOption` links `TableId`+`RowIndex`.
```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using DndMcpAICsharpFun.Domain.Entities;
using FluentAssertions;
namespace DndMcpAICsharpFun.Tests.Entities;
public sealed class CanonicalKnowledgeTests
{
    private static readonly JsonSerializerOptions O = new(JsonSerializerDefaults.Web){ Converters = { new JsonStringEnumConverter() } };
    [Fact]
    public void Table_round_trips_with_provenance()
    {
        var t = new CanonicalTable("phb14.table.draconic-ancestry", "Draconic Ancestry",
            new[]{"ancestry","damageType"},
            new[]{ new CanonicalTableRow(new[]{
                new CanonicalCell("Red", new ProvenanceRef("phb14.block.123","PHB",34)),
                new CanonicalCell("fire", new ProvenanceRef("phb14.block.123","PHB",34)) }) });
        var rt = JsonSerializer.Deserialize<CanonicalTable>(JsonSerializer.Serialize(t,O),O)!;
        rt.Columns.Should().Equal("ancestry","damageType");
        rt.Rows[0].Cells[1].Value.Should().Be("fire");
        rt.Rows[0].Cells[1].Provenance!.Page.Should().Be(34);
    }
}
```
- [ ] **Step 2:** run `dotnet test --filter CanonicalKnowledgeTests` → FAIL (types missing).
- [ ] **Step 3:** create `CanonicalKnowledge.cs` with the six records above (all `public sealed record`, `IReadOnlyList<>` members).
- [ ] **Step 4:** `dotnet build` 0 warnings; `dotnet test --filter CanonicalKnowledgeTests` → PASS.
- [ ] **Step 5: Commit** (pause for go-ahead): `feat(canonical): first-class Table/ChoiceSet/ProvenanceRef shapes`.

---

### Task 2: Extend CanonicalJsonFile + loader to carry Tables/ChoiceSets

**Files:**
- Modify: `Domain/Entities/CanonicalJsonFile.cs` (add optional members to the `CanonicalJsonFile` record)
- Modify: `Features/Entities/CanonicalJsonLoader.cs` (`LoadAsync` validates table/choiceset ids)
- Test: `DndMcpAICsharpFun.Tests/Entities/CanonicalJsonLoaderKnowledgeTests.cs`

**Consumes:** Task 1 types. **Produces:** `CanonicalJsonFile.Tables` (`IReadOnlyList<CanonicalTable>`), `CanonicalJsonFile.ChoiceSets` (`IReadOnlyList<CanonicalChoiceSet>`), defaulting to empty.

- [ ] **Step 1: Failing test** — write a temp canonical JSON file with `Entities: []` plus a `Tables` array (one table) and a `ChoiceSets` array (one set), `LoadAsync` it, assert `file.Tables` has 1 and `file.ChoiceSets[0].Options[0].TableId` matches the table id. Add a second test: duplicate table ids → throws (mirror the existing duplicate-id entity validation).
- [ ] **Step 2:** run → FAIL (members don't exist).
- [ ] **Step 3:** add to the record: `IReadOnlyList<CanonicalTable> Tables = null!, IReadOnlyList<CanonicalChoiceSet> ChoiceSets = null!` with init-normalisation to `[]` (same pattern as `EntityEnvelope.Keywords`). In `LoadAsync`, after the entity-id checks, validate non-empty/unique `Table.Id` and `ChoiceSet.Id` and that each `ChoiceOption.TableId` resolves; throw the same exception type the loader already uses for dup ids.
- [ ] **Step 4:** build 0 warnings; tests PASS (incl. existing loader tests — backward compatible since members default empty).
- [ ] **Step 5: Commit:** `feat(canonical): loader carries + validates Tables/ChoiceSets`.

---

### Task 3: Author the Dragonborn canonical fixture

**Files:**
- Create: `books/canonical/dragonborn-slice.json` (a standalone slice fixture; SchemaVersion "1", Book = PHB metadata, `Entities: []`, the two `Tables` + one `ChoiceSet`)
- Test: `DndMcpAICsharpFun.Tests/Entities/DragonbornFixtureTests.cs`

**Consumes:** Tasks 1–2. **Produces:** the authored ids `phb14.table.draconic-ancestry`, `phb14.table.breath-damage-by-tier`, `phb14.choiceset.draconic-ancestry`.

- [ ] **Step 1: Failing test** — `LoadAsync("books/canonical/dragonborn-slice.json")`: assert the ancestry table has a `Red` row with `damageType=fire`, `breathArea=15-ft cone`, `saveAbility=Dexterity`; the tier table maps tier rows (`1` → `1d10`, `3` → `3d6`); the choice-set has 10 options each pointing at an ancestry row; every cell carries provenance with `SourceBook="PHB"`.
- [ ] **Step 2:** run → FAIL (file missing).
- [ ] **Step 3:** author the JSON. Ancestry table columns `[ancestry, damageType, breathArea, saveAbility]`, 10 rows (Black→acid/5×30 line/Dex, Blue→lightning/5×30 line/Dex, Brass→fire/5×30 line/Dex, Bronze→lightning/5×30 line/Dex, Copper→acid/5×30 line/Dex, Gold→fire/15-ft cone/Dex, Green→poison/15-ft cone/Con, Red→fire/15-ft cone/Dex, Silver→cold/15-ft cone/Con, White→cold/15-ft cone/Con). Tier table columns `[tier, dice]` rows `(1,1d10)(2,2d6)(3,3d6)(4,4d6)`. Provenance: `{blockId, "PHB", 34}` (use the real Dragonborn block id if one exists in dnd_blocks; else a stable placeholder id documented in the file). Choice-set `phb14.choiceset.draconic-ancestry` with one option per ancestry (`Key=ancestry name`, `TableId=…draconic-ancestry`, `RowIndex=i`).
- [ ] **Step 4:** test PASS.
- [ ] **Step 5: Commit:** `data(canonical): author Dragonborn ancestry + breath-tier tables (provenance-linked)`.

---

### Task 4: Postgres structured-fact tables + migration

**Files:**
- Create: `Domain/StructuredFacts.cs` (EF entity records)
- Modify: `Infrastructure/Persistence/AppDbContext.cs` (DbSets + `OnModelCreating` config)
- Create migration under `Migrations/`
- Test: `DndMcpAICsharpFun.Tests/Persistence/StructuredFactsPersistenceTests.cs`

**Produces:** EF entities `StructuredTable(long Id, string CanonicalId, string Name, string ColumnsJson, string SourceBook)`, `StructuredTableRow(long Id, long TableId, int RowIndex, string CellsJson)` (CellsJson = serialized `List<CanonicalCell>` so provenance rides along), `ChoiceSetRow(long Id, string CanonicalId, string Name, string OptionsJson)`. (JSON columns keep the slice's schema tiny; keyed lookup is by `CanonicalId` + `RowIndex`.)

- [ ] **Step 1: Failing persistence test** (Testcontainers harness — copy an existing `*PersistenceTests` class's base/fixture): insert a `StructuredTable` + 2 `StructuredTableRow`s, query by `CanonicalId` + `RowIndex==7`, assert `CellsJson` deserialises to the Red row with provenance. Mark with the persistence test trait the suite already uses.
- [ ] **Step 2:** run the persistence filter → FAIL (entities/DbSets missing).
- [ ] **Step 3:** add the records to `Domain/StructuredFacts.cs`; add `public DbSet<StructuredTable> StructuredTables => Set<StructuredTable>();` (+ rows + choicesets) and `OnModelCreating` config: `HasIndex(t => t.CanonicalId).IsUnique()` on table + choiceset, `HasIndex(r => new { r.TableId, r.RowIndex }).IsUnique()` on rows, `Property(...).HasColumnType("text")` for the JSON columns. Generate the migration: `dotnet ef migrations add AddStructuredFacts` (verify it builds; the InitialCreate pattern is in `/Migrations/`).
- [ ] **Step 4:** persistence test PASS (Docker up); build 0 warnings.
- [ ] **Step 5: Commit:** `feat(persistence): structured-fact tables (Table/Row/ChoiceSet) + migration`.

---

### Task 5: StructuredFactProjector (canonical → Postgres) + admin endpoint

**Files:**
- Create: `Features/Resolution/StructuredFactProjector.cs`
- Modify: `Features/Admin/BooksAdminEndpoints.cs` (add `project-structured` route + handler)
- Modify: `Extensions/ServiceCollectionExtensions.cs` (register the projector)
- Modify: `DndMcpAICsharpFun.http` + `dnd-mcp-api.insomnia.json`
- Test: `DndMcpAICsharpFun.Tests/Persistence/StructuredFactProjectorTests.cs`

**Consumes:** Tasks 1–4 (`CanonicalJsonLoader`, the EF entities, `IDbContextFactory<AppDbContext>`). **Produces:** `StructuredFactProjector.ProjectAsync(CanonicalJsonFile file, CancellationToken ct)` (idempotent upsert by `CanonicalId`).

- [ ] **Step 1: Failing persistence test** — load `dragonborn-slice.json`, `ProjectAsync` it **twice**, assert: row counts are identical after both runs (idempotent); a query for the ancestry table `CanonicalId` + the `Red` row index returns fire/15-ft cone/Dex with provenance.
- [ ] **Step 2:** run → FAIL.
- [ ] **Step 3:** implement `ProjectAsync`: for each `CanonicalTable`, upsert `StructuredTable` (match on `CanonicalId`) + replace its rows (serialize `row.Cells` → `CellsJson`); for each `CanonicalChoiceSet`, upsert `ChoiceSetRow` (`OptionsJson` = serialized options). Use a short-lived `await using` context from the factory. Register `services.AddSingleton<StructuredFactProjector>()`. Add the endpoint: `group.MapPost("/books/{id:int}/project-structured", ProjectStructured).DisableAntiforgery();` with a handler that resolves the book's canonical path (reuse how `IngestEntities`/the orchestrator derives the canonical path — `EntityIdSlug`/`BookKey`), loads it, and calls `ProjectAsync`; return `Results.Ok` with counts (synchronous — the projection is small, no queue). Add the request to the `.http` + `.insomnia.json` (copy an existing `ingest-entities` example, change the path).
- [ ] **Step 4:** persistence test PASS; build 0 warnings.
- [ ] **Step 5: Commit:** `feat(resolution): StructuredFactProjector + POST /admin/books/{id}/project-structured`.

---

### Task 6: CharacterSheet.ResolvedChoices slot

**Files:**
- Modify: `Domain/CharacterSheet.cs` (add the property + nested handling)
- Test: `DndMcpAICsharpFun.Tests/Domain/CharacterSheetResolvedChoicesTests.cs`

**Produces:** `public Dictionary<string,string> ResolvedChoices { get; set; } = new();`

- [ ] **Step 1: Failing tests** — (a) set `sheet.ResolvedChoices["ancestry"]="phb14.choiceset.draconic-ancestry:Red"`, serialize+deserialize with **default** options (the EF converter uses default), assert it survives; (b) **back-compat:** deserialize a JSON string of an old sheet that has NO `ResolvedChoices` key → `ResolvedChoices` is non-null + empty (no exception).
```csharp
var old = "{\"Race\":\"Dragonborn\",\"Level\":3}";
var s = System.Text.Json.JsonSerializer.Deserialize<CharacterSheet>(old, (System.Text.Json.JsonSerializerOptions?)null)!;
s.ResolvedChoices.Should().NotBeNull().And.BeEmpty();
```
- [ ] **Step 2:** run → FAIL.
- [ ] **Step 3:** add `public Dictionary<string,string> ResolvedChoices { get; set; } = new();` to `CharacterSheet`. (Default `System.Text.Json` leaves the property at its initializer when the key is absent → empty dict; no migration needed since the JSON column is schemaless text.)
- [ ] **Step 4:** tests PASS; build 0 warnings.
- [ ] **Step 5: Commit:** `feat(domain): CharacterSheet.ResolvedChoices structured slot (back-compatible)`.

---

### Task 7: Resolution math helpers (tier map · proficiency · DC)

**Files:**
- Create: `Features/Resolution/BreathWeaponRules.cs`
- Test: `DndMcpAICsharpFun.Tests/Resolution/BreathWeaponRulesTests.cs`

**Produces:** `static class BreathWeaponRules { static string DiceForLevel(int level); static int ProficiencyBonus(int level); static int SaveDc(int level, int conMod); }`

- [ ] **Step 1: Failing tests** — `DiceForLevel`: 1→1d10, 5→1d10, 6→2d6, 11→3d6, 16→4d6, 20→4d6. `ProficiencyBonus`: 1→2,4→2,5→3,9→4,13→5,17→6. `SaveDc(11, +3)` → 8+4+3=15; `SaveDc(3,+2)` → 8+2+2=12.
- [ ] **Step 2:** run → FAIL.
- [ ] **Step 3:** implement: `DiceForLevel` = tier map (`<=5`→"1d10", `<=10`→"2d6", `<=15`→"3d6", else "4d6"); `ProficiencyBonus` = `2 + (level - 1) / 4`; `SaveDc` = `8 + ProficiencyBonus(level) + conMod`.
- [ ] **Step 4:** tests PASS; build 0 warnings.
- [ ] **Step 5: Commit:** `feat(resolution): breath-weapon tier/proficiency/DC rules`.

---

### Task 8: CharacterResolutionService

**Files:**
- Create: `Features/Resolution/CharacterResolutionService.cs` (+ result records `ResolvedFact`, `ResolvedComponent`)
- Modify: `Extensions/ServiceCollectionExtensions.cs` (register)
- Test: `DndMcpAICsharpFun.Tests/Resolution/CharacterResolutionServiceTests.cs` (unit, seeded store) + `DndMcpAICsharpFun.Tests/Persistence/CharacterResolutionIntegrationTests.cs` (real Postgres)

**Consumes:** `HeroRepository.GetSnapshotAsync`/`GetByIdAsync`, the structured tables (`IDbContextFactory<AppDbContext>`), `BreathWeaponRules`, Task-6 `ResolvedChoices`. **Produces:** `Task<ResolvedFact> ResolveAsync(long heroSnapshotId, string feature, CancellationToken ct)`; `ResolvedFact(string Feature, string Value, IReadOnlyList<ResolvedComponent> Components, string Confidence)`; `ResolvedComponent(string Label, string Value, ProvenanceRef? Provenance)`.

- [ ] **Step 1: Failing unit test** — with a fake/seeded structured store + a `HeroSnapshot` (Sheet: Race Dragonborn, Constitution 16 → +3, Level 11, `ResolvedChoices["ancestry"]="phb14.choiceset.draconic-ancestry:Red"`), `ResolveAsync(snapshotId,"breath weapon")` → `Value` contains "fire", "15-ft cone", "Dex", "DC 15", "3d6"; `Components` has ≥3 entries each with provenance; resolve at Level 3 → "1d10", DC 12.
- [ ] **Step 2:** run → FAIL.
- [ ] **Step 3:** implement `ResolveAsync`: load snapshot → parse `ResolvedChoices["ancestry"]` (`choicesetRef:Red`) → look up the choice-set option → ancestry table row (`damageType/breathArea/saveAbility` + provenance) → `BreathWeaponRules.DiceForLevel(sheet.Level)` (provenance = tier-table cell) → `BreathWeaponRules.SaveDc(sheet.Level, Modifier(sheet.Constitution))` → assemble `ResolvedFact` (Value = rendered string, Components = [ancestry-derived, dice, DC]). If a needed row is missing/`needsReview`, set that component's value to the prose span and `Confidence="needsReview"` (slice: stub the prose-span fetch to return the provenance's blockId text or a placeholder — full Qdrant fetch is out of scope, but the fallback branch + shape must exist and be tested). Register `services.AddSingleton<CharacterResolutionService>()`.
- [ ] **Step 4:** unit PASS.
- [ ] **Step 5: Integration test** (Testcontainers): project `dragonborn-slice.json`, seed a Red Dragonborn snapshot at L3/L11/L17, `ResolveAsync` → correct cited breath weapon at each level. Run the persistence filter → PASS.
- [ ] **Step 6:** build 0 warnings.
- [ ] **Step 7: Commit:** `feat(resolution): CharacterResolutionService (cited breath-weapon resolution)`.

---

### Task 9: MCP tool resolve_character_feature

**Files:**
- Modify: `Features/Mcp/DndMcpTools.cs` (add the tool + inject `CharacterResolutionService`)
- Test: `DndMcpAICsharpFun.Tests/Mcp/ResolveCharacterFeatureToolTests.cs`

**Consumes:** `CharacterResolutionService.ResolveAsync`. **Produces:** `[McpServerTool] Task<string> resolve_character_feature(long heroSnapshotId, string feature, CancellationToken ct)` returning the JSON-serialised `ResolvedFact`.

- [ ] **Step 1: Failing test** — construct `DndMcpTools` with a real/seeded `CharacterResolutionService` (or a thin fake), call `resolve_character_feature(snapshotId,"breath weapon")`, assert the returned JSON contains the cited value + components with provenance.
- [ ] **Step 2:** run → FAIL.
- [ ] **Step 3:** add `CharacterResolutionService` to the `DndMcpTools` primary constructor; add the `[McpServerTool, Description("Compute a character-specific, cited rule fact (e.g. \"breath weapon\") for a hero snapshot.")]` method delegating to `ResolveAsync` and returning `JsonSerializer.Serialize(fact, …)`. (No HTTP route — it's an MCP tool via `.WithToolsFromAssembly()`, so no `.http` change; confirm the constructor change doesn't break existing tool registration.)
- [ ] **Step 4:** test PASS; build 0 warnings; full non-persistence suite green.
- [ ] **Step 5: Commit:** `feat(mcp): resolve_character_feature tool (computed, cited character facts)`.

---

## Self-Review

- **Spec coverage:** structured-knowledge-store {Table/ChoiceSet shapes → T1-2; provenance → T1,3,5; projection+keyed lookup → T4-5} ✓. character-fact-resolution {ResolvedChoices slot + back-compat → T6; deterministic resolve + tier/DC + provenance + fallback → T7-8; MCP tool → T9} ✓. Every spec scenario maps to a test.
- **Placeholder scan:** none — formulas, fixtures, and test cases are concrete. (The prose-span fetch in T8 is explicitly scoped to a stub with the fallback shape tested — not a hidden TODO.)
- **Type consistency:** `CanonicalTable/Cell/ChoiceSet/ProvenanceRef` (T1) used verbatim in T2-5,8; `BreathWeaponRules.{DiceForLevel,ProficiencyBonus,SaveDc}` (T7) used in T8; `ResolvedFact/ResolvedComponent` (T8) used in T9; `ResolvedChoices` (T6) read in T8; `StructuredTable/Row/ChoiceSetRow` (T4) used in T5,8. Consistent.
- **Decomposition note:** the slice is one vertical cut; tasks are sequenced write→store→read with each ending in a tested deliverable. Persistence tasks (4,5,8-integration) need Docker.
