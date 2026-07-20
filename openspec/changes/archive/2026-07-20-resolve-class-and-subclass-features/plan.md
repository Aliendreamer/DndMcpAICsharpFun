# Resolve Class & Subclass Features — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Add two grounded, cited resolvers to `CharacterResolutionService` — `"class features"` (from the projected class tables) and `"subclass spells"` (from a new subclass-spells projection).

**Architecture:** Both resolvers read Postgres `StructuredTables` (the resolution store), iterating `sheet.Classes[]` per class. `"class features"` reuses the already-projected `*.table.<class-slug>` tables. `"subclass spells"` needs a new `SubclassSpellsProjector` (console step composed by `ProjectTablesRunner`, like `DraconicAncestryResolutionProjector`) that projects 5etools `additionalSpells` into `*.table.<subclass-slug>-spells` tables landed by `StructuredFactProjector`.

**Tech Stack:** C# / .NET 10, System.Text.Json, EF Core (Npgsql), xunit + FluentAssertions, Testcontainers Postgres. Serena MCP for all `.cs`.

## Global Constraints

- **Serena only** for all `.cs` reads/edits; grep-verify after each edit (REAL newlines; `replace_symbol_body` has dropped `[Fact]`/attributes before). Built-in Read/Edit on `.cs` forbidden.
- **Work on `main`**; commit each task after its reviewer passes.
- `net10.0`, nullable, **warnings-as-errors** → every task `dotnet build` 0/0.
- `dotnet` FAILS under the sandbox (git-crypt) → run all `dotnet build`/`test`/`run` with `dangerouslyDisableSandbox: true`.
- Ignore LSP false diagnostics (e.g. duplicate `using Xunit` CS8019); trust `dotnet build`.
- Run `dotnet format DndMcpAICsharpFun.slnx --include <your new/changed files>` before the final gate (per-task builds don't check format; repo carries pre-existing violations in OTHER files — verify only YOUR files are clean).
- No new HTTP endpoint → `DndMcpAICsharpFun.http` / `dnd-mcp-api.insomnia.json` untouched.

**Key existing types & patterns (verbatim):**
```csharp
public sealed record ResolvedComponent(string Label, string Value, ProvenanceRef? Provenance);
public sealed record ResolvedFact(string Feature, string Value, IReadOnlyList<ResolvedComponent> Components, string Confidence);
public sealed class CharacterResolutionService(IDbContextFactory<AppDbContext> dbf, HeroRepository heroes) { ... }
// CharacterSheet: List<ClassLevel> Classes;  ClassLevel { string Class; string Subclass; int Level; }
//   sheet.Class/Subclass = Classes[0].*;  sheet.Level = sum of class levels.
// Read a table (from ResolveSpellSlotsAsync):
//   var table = await db.StructuredTables.FirstOrDefaultAsync(t => t.CanonicalId == id, ct);
//   var row   = await db.StructuredTableRows.FirstOrDefaultAsync(r => r.TableId == table.Id && r.RowIndex == n, ct);
//   var columns = JsonSerializer.Deserialize<List<string>>(table.ColumnsJson, JsonOpts) ?? [];
//   var cells   = JsonSerializer.Deserialize<List<CanonicalCell>>(row.CellsJson, JsonOpts) ?? [];
// Dispatch in ResolveForSheetAsync (feature.Equals(..., OrdinalIgnoreCase) => ResolveXAsync(sheet, ct)).
// CanonicalTable(string Id, string Name, IReadOnlyList<string> Columns, IReadOnlyList<CanonicalTableRow> Rows);
// CanonicalTableRow(IReadOnlyList<CanonicalCell> Cells);  CanonicalCell(string Value, ProvenanceRef? Provenance);
// EntityIdSlug.Table(bookKey, name) => "<bookslug>.table.<name-slug>".
```

**5etools `additionalSpells` shapes (verbatim):**
- `prepared`/`known`: keys are CHARACTER levels — `{"prepared":{"1":["bless","cure wounds"],"3":[...]}}`.
- `expanded` (Warlock only): keys are SPELL levels `s<N>` — `{"expanded":{"s1":["burning hands","command"],"s2":[...]}}`; spell level N is available at Warlock character level `2*N - 1` (s1→1, s2→3, s3→5, s4→7, s5→9).

---

## Task 1: `EntityIdSlug.Slug` + `SubclassSpellsProjector`

**Files:**
- Modify: `Domain/Entities/EntityIdSlug.cs` (expose a name-slug helper)
- Create: `Features/Ingestion/FivetoolsIngestion/SubclassSpellsProjector.cs`
- Test: `DndMcpAICsharpFun.Tests/Ingestion/Fivetools/SubclassSpellsProjectorTests.cs`

**Interfaces:**
- Produces:
  - `EntityIdSlug.Slug(string name) : string` — the bare name-slug (e.g. `"Life Domain"` → `"life-domain"`), reusing the private `SlugifyName`.
  - `static class SubclassSpellsProjector { IReadOnlyList<CanonicalTable> Project(string fivetoolsDir, string sourceKey) }` — scans `<fivetoolsDir>/class/class-*.json` `subclass[]` entries with `source==sourceKey`; for each with a usable `additionalSpells`, emits `CanonicalTable` id `EntityIdSlug.Table(sourceKey, "<subclass name> spells")`... **NO** — id must be `<bookslug>.table.<subclass-slug>-spells`: build as `$"{EntityIdSlug.Slug... }"`; use `$"{bookSlug}.table.{EntityIdSlug.Slug(subclassName)}-spells"` where `bookSlug = EntityIdSlug.Table(sourceKey,"x").Split('.')[0]`. Columns `["level","spells"]`, one row per grant-level (`spells` = comma-joined, unioned across `prepared`/`known`/`expanded`), rows sorted ascending by level. Subclass with no usable grant → no table.

- [ ] **Step 1: Write the failing test**

```csharp
using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Ingestion.Fivetools;

public class SubclassSpellsProjectorTests
{
    private static string WriteFixture()
    {
        var dir = Path.Combine(Path.GetTempPath(), "5scs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "class"));
        File.WriteAllText(Path.Combine(dir, "class", "class-cleric.json"), """
        {"subclass":[{"name":"Life Domain","shortName":"Life","className":"Cleric","source":"PHB","page":60,
          "additionalSpells":[{"prepared":{"1":["bless","cure wounds"],"3":["lesser restoration","spiritual weapon"]}}]},
          {"name":"Order Domain","className":"Cleric","source":"PHB"}]}
        """);
        File.WriteAllText(Path.Combine(dir, "class", "class-warlock.json"), """
        {"subclass":[{"name":"The Fiend","className":"Warlock","source":"PHB",
          "additionalSpells":[{"expanded":{"s1":["burning hands","command"],"s3":["fireball","stinking cloud"]}}]}]}
        """);
        return dir;
    }

    [Fact]
    public void Projects_prepared_and_expanded_subclass_spells()
    {
        var dir = WriteFixture();
        try
        {
            var tables = SubclassSpellsProjector.Project(dir, "PHB");
            var ids = tables.Select(t => t.Id).ToList();
            ids.Should().Contain("phb14.table.life-domain-spells");
            ids.Should().Contain("phb14.table.the-fiend-spells");
            ids.Should().NotContain(i => i.Contains("order-domain")); // no additionalSpells → no table

            var life = tables.Single(t => t.Id == "phb14.table.life-domain-spells");
            life.Columns.Should().Equal("level", "spells");
            life.Rows[0].Cells[0].Value.Should().Be("1");
            life.Rows[0].Cells[1].Value.Should().Contain("bless").And.Contain("cure wounds");

            // expanded: s1 -> char level 1, s3 -> char level 5
            var fiend = tables.Single(t => t.Id == "phb14.table.the-fiend-spells");
            fiend.Rows.Select(r => r.Cells[0].Value).Should().Contain("1").And.Contain("5");
            fiend.Rows.Single(r => r.Cells[0].Value == "5").Cells[1].Value.Should().Contain("fireball");
        }
        finally { Directory.Delete(dir, true); }
    }
}
```

- [ ] **Step 2: Run → confirm FAIL** (types missing). `dotnet test DndMcpAICsharpFun.Tests --filter FullyQualifiedName~SubclassSpellsProjectorTests` (dangerouslyDisableSandbox).

- [ ] **Step 3: Add `EntityIdSlug.Slug`** (Serena `insert_after_symbol` after `Table`):

```csharp
    /// <summary>The bare name-slug (e.g. "Life Domain" → "life-domain"), for building/matching table id suffixes.</summary>
    public static string Slug(string name) => SlugifyName(name);
```

- [ ] **Step 4: Create `SubclassSpellsProjector.cs`**

```csharp
using System.Text.Json;
using System.Text.RegularExpressions;
using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;

/// <summary>Projects each spellcasting subclass's 5etools additionalSpells into a
/// &lt;slug&gt;.table.&lt;subclass-slug&gt;-spells CanonicalTable (cols level|spells) for a projected book.</summary>
public static partial class SubclassSpellsProjector
{
    [GeneratedRegex(@"^s(\d+)$")] private static partial Regex SpellLevelKey();

    public static IReadOnlyList<CanonicalTable> Project(string fivetoolsDir, string sourceKey)
    {
        var bookSlug = EntityIdSlug.Table(sourceKey, "x").Split('.')[0];
        var classDir = Path.Combine(fivetoolsDir, "class");
        if (!Directory.Exists(classDir)) return [];

        var tables = new List<CanonicalTable>();
        foreach (var file in Directory.EnumerateFiles(classDir, "class-*.json"))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            if (!doc.RootElement.TryGetProperty("subclass", out var subs) || subs.ValueKind != JsonValueKind.Array)
                continue;
            foreach (var sc in subs.EnumerateArray())
            {
                if (!(sc.TryGetProperty("source", out var s) && s.ValueKind == JsonValueKind.String && s.GetString() == sourceKey))
                    continue;
                if (!sc.TryGetProperty("name", out var nm) || nm.ValueKind != JsonValueKind.String) continue;
                if (!sc.TryGetProperty("additionalSpells", out var add) || add.ValueKind != JsonValueKind.Array) continue;

                var byLevel = new SortedDictionary<int, List<string>>();
                foreach (var grant in add.EnumerateArray())
                {
                    if (grant.ValueKind != JsonValueKind.Object) continue;
                    foreach (var kind in new[] { "prepared", "known", "expanded" })
                    {
                        if (!grant.TryGetProperty(kind, out var m) || m.ValueKind != JsonValueKind.Object) continue;
                        foreach (var lvl in m.EnumerateObject())
                        {
                            if (!TryLevel(kind, lvl.Name, out var level)) continue; // skip choose/ability keys
                            if (lvl.Value.ValueKind != JsonValueKind.Array) continue;
                            var list = byLevel.TryGetValue(level, out var l) ? l : byLevel[level] = new();
                            foreach (var sp in lvl.Value.EnumerateArray())
                                if (sp.ValueKind == JsonValueKind.String) list.Add(sp.GetString()!);
                        }
                    }
                }
                if (byLevel.Count == 0) continue;

                var page = sc.TryGetProperty("page", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : (int?)null;
                var prov = new ProvenanceRef($"{bookSlug}.5etools", sourceKey, page);
                var rows = byLevel.Select(kv => new CanonicalTableRow(
                    new List<CanonicalCell>
                    {
                        new(kv.Key.ToString(), prov),
                        new(string.Join(", ", kv.Value.Distinct()), prov),
                    })).ToList();
                var id = $"{bookSlug}.table.{EntityIdSlug.Slug(nm.GetString()!)}-spells";
                tables.Add(new CanonicalTable(id, $"{nm.GetString()} Spells", new List<string> { "level", "spells" }, rows));
            }
        }
        return tables;
    }

    // prepared/known keys are character levels; expanded keys are "s<N>" (spell level N ⇒ Warlock level 2N-1).
    private static bool TryLevel(string kind, string key, out int level)
    {
        if (kind == "expanded")
        {
            var m = SpellLevelKey().Match(key);
            if (m.Success) { level = 2 * int.Parse(m.Groups[1].Value) - 1; return true; }
            level = 0; return false;
        }
        return int.TryParse(key, out level);
    }
}
```

- [ ] **Step 5: Run tests → PASS. `dotnet build` → 0/0.**
- [ ] **Step 6: Commit:** `feat(resolution): SubclassSpellsProjector (additionalSpells → per-subclass spells table)`

---

## Task 2: Console wiring for subclass-spells tables

**Files:**
- Modify: `Features/Ingestion/FivetoolsIngestion/ProjectTablesRunner.cs`
- Modify: `DndMcpAICsharpFun.Tests/Ingestion/Fivetools/ProjectTablesConsoleTests.cs`

**Interfaces:**
- Consumes: `SubclassSpellsProjector.Project`.
- Produces: `RunOneAsync` composes subclass-spells tables into the projected set alongside generic + draconic-resolution tables. Subclass-spells ids (`*-spells`) don't collide with class/captioned ids, so they append after the existing cede/dedup. The empty-projection skip guard still applies to the composed total.

- [ ] **Step 1: Update the test** — extend `Fixtures.OfficialBook`'s 5etools dir with a subclass carrying `additionalSpells`, and assert a `*-spells` table appears after `RunOneAsync`. Add to `class/class-cleric.json` in the fixture:
```csharp
            File.WriteAllText(Path.Combine(fiveDir, "class", "class-cleric.json"), """
            {"subclass":[{"name":"Life Domain","className":"Cleric","source":"PHB",
              "additionalSpells":[{"prepared":{"1":["bless","cure wounds"]}}]}]}
            """);
```
and in the official test add:
```csharp
            reloaded.Tables.Select(t => t.Id).Should().Contain("phb14.table.life-domain-spells");
```

- [ ] **Step 2: Run → the official test FAILS** (runner doesn't emit subclass-spells yet). Focused: `~ProjectTablesConsoleTests`.

- [ ] **Step 3: Update `RunOneAsync`** — Serena `replace_symbol_body`. After composing `tables` (generic-minus-owned + resolution) and BEFORE the empty-skip guard, append subclass-spells:

```csharp
        var ownedIds = resolution.Tables.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
        var subclassSpells = SubclassSpellsProjector.Project(fivetoolsDir, key);
        var tables = generic.Where(t => !ownedIds.Contains(t.Id))
            .Concat(resolution.Tables)
            .Concat(subclassSpells)
            .ToList();

        if (tables.Count == 0)
            return new ProjectResult(Skipped: true, SkipReason: "no projectable tables", TableCount: 0);
        // ... (choiceSets + write + round-trip unchanged)
```

- [ ] **Step 4: Run tests → both `ProjectTablesConsoleTests` pass. Whole-solution `dotnet build` → 0/0.**
- [ ] **Step 5: Commit:** `feat(resolution): wire subclass-spells tables into ProjectTables console`

---

## Task 3: `"class features"` resolver

**Files:**
- Modify: `Features/Resolution/CharacterResolutionService.cs`
- Test: `DndMcpAICsharpFun.Tests/Persistence/ClassFeaturesResolutionIntegrationTests.cs` (real Postgres — the resolver reads `StructuredTables`)

**Interfaces:**
- Produces: `private async Task<ResolvedFact> ResolveClassFeaturesAsync(CharacterSheet sheet, CancellationToken ct)` + a dispatch line `if (feature.Equals("class features", StringComparison.OrdinalIgnoreCase)) return ResolveClassFeaturesAsync(sheet, ct);` in `ResolveForSheetAsync`.
- Behavior: per `c in sheet.Classes`, find `StructuredTables` where `CanonicalId` EndsWith `$".table.{EntityIdSlug.Slug(c.Class)}"`; read rows `RowIndex < c.Level` (levels 1..c.Level), split each `Features` cell (index 2) by `", "` into the cumulative list, and read the `Proficiency Bonus` cell (index 1) from `RowIndex == c.Level - 1`. One `ResolvedComponent(c.Class, "<cumulative features summary> (prof <pb>)", <a cell's provenance>)` per class. A class whose table is absent → its component value `"[table not found]"` and overall `Confidence="needsReview"`.

- [ ] **Step 1: Write the failing test** (seed a class table, resolve). Mirror `ProjectedDraconicResolutionIntegrationTests` for the Postgres harness (`[Collection("postgres")]`, `PostgresFixture`, `DbFactory`, seed via `StructuredFactProjector`). Seed by projecting the real PHB class table for Fighter:

```csharp
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Campaigns;
using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;
using DndMcpAICsharpFun.Features.Resolution;
using DndMcpAICsharpFun.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DndMcpAICsharpFun.Tests.Persistence;

[Collection("postgres")]
public sealed class ClassFeaturesResolutionIntegrationTests(PostgresFixture pg) : IAsyncLifetime
{
    public Task InitializeAsync() => pg.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private IDbContextFactory<AppDbContext> DbFactory()
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<AppDbContext>(o => o.UseNpgsql(pg.Container.GetConnectionString()));
        return services.BuildServiceProvider().GetRequiredService<IDbContextFactory<AppDbContext>>();
    }

    [Fact]
    public async Task Class_features_resolve_for_l6_fighter()
    {
        // Land the real projected Fighter class table.
        var dbf = DbFactory();
        var classTables = new FivetoolsTableProjection().BuildForBook(TestPaths.RepoFile("5etools"), "PHB");
        var fighter = classTables.Single(t => t.Id == "phb14.table.fighter");
        var file = new CanonicalJsonFile("1", new CanonicalBookMetadata("PHB","Edition2014","x","PHB"), [], new[]{ fighter }, []);
        await new StructuredFactProjector(dbf).ProjectAsync(file, CancellationToken.None);

        var sheet = new CharacterSheet { Classes = [new ClassLevel { Class = "Fighter", Level = 6 }] };
        await using var db = pg.NewContext();
        db.HeroSnapshots.Add(new HeroSnapshot(0, 1, 1, "F6", 6, DateTime.UtcNow, sheet));
        await db.SaveChangesAsync();
        var snapId = await db.HeroSnapshots.Where(s => s.HeroId == 1).Select(s => s.Id).FirstAsync();

        var svc = new CharacterResolutionService(dbf, new HeroRepository(dbf));
        var fact = await svc.ResolveAsync(snapId, "class features");

        fact.Confidence.Should().Be("ok");
        fact.Value.Should().Contain("Extra Attack").And.Contain("Ability Score Improvement");
        fact.Value.Should().Contain("+3"); // L6 proficiency bonus
        fact.Components.Should().ContainSingle(c => c.Label == "Fighter");
    }
}
```

- [ ] **Step 2: Run → FAIL** (`class features` unsupported → NotSupportedException). Focused: `~ClassFeaturesResolutionIntegrationTests` (Docker up).

- [ ] **Step 3: Add the resolver + dispatch** — Serena `insert_after_symbol` (after `ResolveSpellAttackAsync`) and `replace_symbol_body` on `ResolveForSheetAsync` to add the dispatch line.

```csharp
    private async Task<ResolvedFact> ResolveClassFeaturesAsync(CharacterSheet sheet, CancellationToken ct)
    {
        await using var db = await dbf.CreateDbContextAsync(ct);
        var components = new List<ResolvedComponent>();
        var rendered = new List<string>();
        var confidence = "ok";

        foreach (var c in sheet.Classes)
        {
            if (string.IsNullOrWhiteSpace(c.Class)) continue;
            var suffix = $".table.{EntityIdSlug.Slug(c.Class)}";
            var table = await db.StructuredTables.FirstOrDefaultAsync(t => t.CanonicalId.EndsWith(suffix), ct);
            if (table is null)
            {
                confidence = "needsReview";
                components.Add(new ResolvedComponent(c.Class, "[class table not found]", null));
                continue;
            }
            var rows = await db.StructuredTableRows
                .Where(r => r.TableId == table.Id && r.RowIndex < c.Level)
                .OrderBy(r => r.RowIndex).ToListAsync(ct);
            ProvenanceRef? prov = null;
            var byLevel = new List<string>();
            string prof = "";
            foreach (var r in rows)
            {
                var cells = JsonSerializer.Deserialize<List<CanonicalCell>>(r.CellsJson, JsonOpts) ?? [];
                prov ??= cells.Count > 0 ? cells[0].Provenance : null;
                var feats = cells.Count > 2 ? cells[2].Value : "";
                if (!string.IsNullOrWhiteSpace(feats)) byLevel.Add($"L{r.RowIndex + 1}: {feats}");
                if (r.RowIndex == c.Level - 1 && cells.Count > 1) prof = cells[1].Value;
            }
            var summary = byLevel.Count > 0 ? string.Join("; ", byLevel) : "no features";
            var val = string.IsNullOrWhiteSpace(prof) ? summary : $"{summary} (proficiency bonus {prof})";
            components.Add(new ResolvedComponent(c.Class, val, prov));
            rendered.Add($"{c.Class}: {val}");
        }

        if (components.Count == 0)
            return new ResolvedFact("class features", "no classes", [], "needsReview");
        return new ResolvedFact("class features", string.Join(" | ", rendered), components, confidence);
    }
```
Dispatch line (add before the `throw`):
```csharp
        if (feature.Equals("class features", StringComparison.OrdinalIgnoreCase))
            return ResolveClassFeaturesAsync(sheet, ct);
```

- [ ] **Step 4: Run tests → PASS. `dotnet build` → 0/0.**
- [ ] **Step 5: Commit:** `feat(resolution): "class features" resolver over projected class tables`

---

## Task 4: `"subclass spells"` resolver

**Files:**
- Modify: `Features/Resolution/CharacterResolutionService.cs`
- Test: `DndMcpAICsharpFun.Tests/Persistence/SubclassSpellsResolutionIntegrationTests.cs`

**Interfaces:**
- Produces: `private async Task<ResolvedFact> ResolveSubclassSpellsAsync(CharacterSheet sheet, CancellationToken ct)` + dispatch `if (feature.Equals("subclass spells", StringComparison.OrdinalIgnoreCase)) return ResolveSubclassSpellsAsync(sheet, ct);`.
- Behavior: per `c in sheet.Classes` with non-empty `c.Subclass`, find `StructuredTables` where `CanonicalId` EndsWith `$".table.{EntityIdSlug.Slug(c.Subclass)}-spells"`; collect rows whose `level` cell (index 0, int) ≤ `c.Level`, union the `spells` cell (index 1). Absent table → `needsReview` component; table present but no rows ≤ level → value "none", confidence stays `ok`. One component per subclass.

- [ ] **Step 1: Write the failing test** — project the real PHB subclass-spells (via `SubclassSpellsProjector`), land via `StructuredFactProjector`, resolve a L5 Life Cleric.

```csharp
    [Fact]
    public async Task Subclass_spells_resolve_for_l5_life_cleric()
    {
        var dbf = DbFactory();
        var scTables = SubclassSpellsProjector.Project(TestPaths.RepoFile("5etools"), "PHB");
        var life = scTables.Single(t => t.Id == "phb14.table.life-domain-spells");
        var file = new CanonicalJsonFile("1", new CanonicalBookMetadata("PHB","Edition2014","x","PHB"), [], new[]{ life }, []);
        await new StructuredFactProjector(dbf).ProjectAsync(file, CancellationToken.None);

        var sheet = new CharacterSheet { Classes = [new ClassLevel { Class = "Cleric", Subclass = "Life Domain", Level = 5 }] };
        await using var db = pg.NewContext();
        db.HeroSnapshots.Add(new HeroSnapshot(0, 2, 1, "Life5", 5, DateTime.UtcNow, sheet));
        await db.SaveChangesAsync();
        var snapId = await db.HeroSnapshots.Where(s => s.HeroId == 2).Select(s => s.Id).FirstAsync();

        var svc = new CharacterResolutionService(dbf, new HeroRepository(dbf));
        var fact = await svc.ResolveAsync(snapId, "subclass spells");

        fact.Confidence.Should().Be("ok");
        fact.Value.Should().Contain("bless").And.Contain("cure wounds")
            .And.Contain("lesser restoration").And.Contain("beacon of hope").And.Contain("revivify");
        fact.Value.Should().NotContain("death ward"); // L7 grant, excluded at L5
    }
```
(Reuse the `[Collection("postgres")]` harness — put both integration tests in the same file OR mirror the `DbFactory` helper; a shared `PostgresFixture` file already exists.)

- [ ] **Step 2: Run → FAIL** (`subclass spells` unsupported).

- [ ] **Step 3: Add the resolver + dispatch:**

```csharp
    private async Task<ResolvedFact> ResolveSubclassSpellsAsync(CharacterSheet sheet, CancellationToken ct)
    {
        await using var db = await dbf.CreateDbContextAsync(ct);
        var components = new List<ResolvedComponent>();
        var confidence = "ok";

        foreach (var c in sheet.Classes)
        {
            if (string.IsNullOrWhiteSpace(c.Subclass)) continue;
            var suffix = $".table.{EntityIdSlug.Slug(c.Subclass)}-spells";
            var table = await db.StructuredTables.FirstOrDefaultAsync(t => t.CanonicalId.EndsWith(suffix), ct);
            if (table is null)
            {
                confidence = "needsReview";
                components.Add(new ResolvedComponent(c.Subclass, "[subclass spells table not found]", null));
                continue;
            }
            var rows = await db.StructuredTableRows
                .Where(r => r.TableId == table.Id).OrderBy(r => r.RowIndex).ToListAsync(ct);
            ProvenanceRef? prov = null;
            var spells = new List<string>();
            foreach (var r in rows)
            {
                var cells = JsonSerializer.Deserialize<List<CanonicalCell>>(r.CellsJson, JsonOpts) ?? [];
                if (cells.Count < 2 || !int.TryParse(cells[0].Value, out var lvl) || lvl > c.Level) continue;
                prov ??= cells[1].Provenance;
                spells.AddRange(cells[1].Value.Split(", ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            }
            var val = spells.Count > 0 ? string.Join(", ", spells.Distinct()) : "none";
            components.Add(new ResolvedComponent(c.Subclass, val, prov));
        }

        if (components.Count == 0)
            return new ResolvedFact("subclass spells", "no subclass", [], "needsReview");
        var value = string.Join(" | ", components.Select(x => $"{x.Label}: {x.Value}"));
        return new ResolvedFact("subclass spells", value, components, confidence);
    }
```
Dispatch line before the `throw`.

- [ ] **Step 4: Run tests → PASS. `dotnet build` → 0/0.**
- [ ] **Step 5: Commit:** `feat(resolution): "subclass spells" resolver over projected subclass-spells tables`

---

## Task 5: Tool surface

**Files:**
- Modify: `Features/Chat/DndChatService.cs` (the `resolve_character_feature` description)
- Modify: `Features/Chat/Routing/QueryRouterOptions.cs` (add example phrases, if that's where feature examples live)

- [ ] **Step 1:** Serena-edit the `resolve_character_feature` tool description string to append: `", \"class features\" (base-class features by level + proficiency bonus, per class), \"subclass spells\" (spells a spellcasting subclass grants, up to the character's level)"` to the supported-features sentence.
- [ ] **Step 2:** Add `"what are my class features"` and `"what spells does my subclass give me"` to the resolver tool-group examples in `QueryRouterOptions` (match the existing `"what is my breath weapon"` entry's format). If the router file's shape differs, mirror the nearest existing example verbatim.
- [ ] **Step 3:** `dotnet build` → 0/0. Run any `DndChatServiceTests` / router tests that assert the tool description or example set (`dotnet test --filter FullyQualifiedName~DndChatService`); update expected strings if a test pins them.
- [ ] **Step 4: Commit:** `feat(chat): advertise class-features + subclass-spells in resolve_character_feature`

---

## Task 6: Corpus run + gates

**Files:** `books/canonical/*.json` (data — new `*-spells` tables), verification only otherwise.

- [ ] **Step 1: Run `ProjectTables --all`** (`dotnet run --project Tools/ProjectTables -- --all`, dangerouslyDisableSandbox). Expect the projected books' table counts to RISE by their subclass-spells count; skipped books unchanged.
- [ ] **Step 2: Verify** — a Python check: each projected canonical reloads, ids unique, `phb14.table.life-domain-spells` present with a level-1 bless/cure-wounds row; class tables unchanged (diff the class-table rows vs HEAD). Confirm `dragonborn-slice.json` still excluded (untouched).
- [ ] **Step 3: Full gates** — `dotnet build` 0/0; FULL `dotnet test` (Docker) green, note the count; `dotnet format DndMcpAICsharpFun.slnx --include <all new/changed files>` clean.
- [ ] **Step 4: Confirm** `git diff --stat` shows only `Features/Resolution`, `Features/Ingestion/FivetoolsIngestion`, `Features/Chat`, `Domain/Entities/EntityIdSlug.cs`, tests, and `books/canonical/*.json`; `.http`/insomnia untouched. No new spoofable-id tool surface (both features route through the existing `ResolveForUserAsync`).
- [ ] **Step 5: Commit** the canonical data: `data(resolution): project subclass-spells tables into official canonicals`.

---

## Self-Review notes (coverage)

- Spec "Resolve class features by level" → Task 3 (+ live in 6). "Resolve subclass granted spells" → Tasks 1,2,4. "tool advertises new features" → Task 5. "Project subclass additionalSpells" → Tasks 1,2 (+ corpus in 6).
- Type consistency: `SubclassSpellsProjector.Project(string,string)`, `EntityIdSlug.Slug(string)`, `ResolveClassFeaturesAsync`/`ResolveSubclassSpellsAsync(CharacterSheet,CancellationToken)`, table id `<bookslug>.table.<slug>-spells`, EndsWith-suffix lookup used identically in projector + both resolvers.
- Grounding contract (absent→needsReview, empty→honest none/value) applied in both resolvers per spec.
- No HTTP surface; per-user features go through existing `ResolveForUserAsync` (Task 6 Step 4).
