# Resolution Features — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Add three new grounded features to `CharacterResolutionService` — `class resources`, `saving throws`, `spell count` — reusing already-projected class tables + PHB rules, with provenance and honest `needsReview` (never fabrication).

**Architecture:** Each feature is a new branch in `ResolveForSheetAsync` + a `Resolve…` method returning `ResolvedFact`. `class resources` and `spell count` read `StructuredTables` (same suffix-match + Take(2) ambiguity guard as `ResolveClassFeaturesAsync`); `saving throws` is a pure static (no DB), like `ResolveMulticlassValidity`. One new static map (`SavingThrowProficiencies`).

**Tech Stack:** C# / .NET 10, xunit + FluentAssertions, real-Postgres integration tests via `PostgresFixture`. Serena for all `.cs`.

## Global Constraints
- **Serena only** for tracked files (subagents too); stop after one >2-min Serena hang. Built-in Read/Edit forbidden.
- **Work on `main`**; commit each reviewed task. Warnings-as-errors → build 0/0. `dotnet` needs `dangerouslyDisableSandbox: true` (git-crypt). Ignore LSP false CS0246 on tests.
- **No HTTP endpoint change**, no `CharacterSheet`/schema change, no `.http`/insomnia change. Armor Class is OUT (deferred to `structured-armor-and-ac`).
- Grounding contract: value when grounded, `"needsReview"` when a required table/row is absent or ambiguous, honest `"none"`/`"no spellcasting"` where the concept legitimately has no value — **never a fabricated number**.
- Integration tests need Docker (Testcontainers Postgres). Docker is up.

**Known shapes (verified):**
```csharp
// ResolvedFact(string Feature, string Value, IReadOnlyList<ResolvedComponent> Components, string Confidence);
// ResolvedComponent(string Label, string Value, ProvenanceRef? Provenance);
// CharacterResolutionService(IDbContextFactory<AppDbContext> dbf, HeroRepository heroes); JsonOpts field exists.
//   ResolveForSheetAsync(sheet, feature, ct) dispatches feature strings; throws NotSupportedException on unknown.
//   Existing table read (ResolveClassFeaturesAsync): suffix = $".table.{EntityIdSlug.Slug(c.Class)}";
//     db.StructuredTables.Where(t => t.CanonicalId.EndsWith(suffix)).Take(2).ToListAsync(ct);  // 0 -> needsReview, >1 -> ambiguous needsReview
//     columns = JsonSerializer.Deserialize<List<string>>(table.ColumnsJson, JsonOpts); rows via StructuredTableRows (RowIndex, CellsJson=List<CanonicalCell>).
//   Existing pure static: public static ResolvedFact ResolveMulticlassValidity(CharacterSheet sheet, string targetClass).
// MulticlassSpellcasting: enum CasterType { None, Full, Half, Third, Pact }; CasterType Classify(ClassLevel c) (Warlock->Pact);
//   string? SpellcastingAbility(string @class) -> full ability name ("Wisdom"/"Intelligence"/"Charisma"/…) or null.
// CharacterSheet: int Strength..Charisma; static int Modifier(int); int ProficiencyBonus; List<ClassLevel> Classes (Classes[0]=primary).
// EntityIdSlug.Slug(string name).
// Class-table columns (verified in phb14.json): barbarian [Level,Proficiency Bonus,Features,Rages,Rage Damage];
//   monk [...,Martial Arts,Ki Points,Unarmored Movement]; sorcerer [...,Sorcery Points,Cantrips Known,Spells Known,1st..9th];
//   bard [...,Cantrips Known,Spells Known,1st..9th]; rogue [...,Sneak Attack]; warlock [...,Cantrips Known,Spells Known,Spell Slots,Slot Level,Invocations Known];
//   cleric/wizard/druid [...,Cantrips Known,1st..9th] (NO Spells Known -> prepared); fighter [Level,Proficiency Bonus,Features] (no resource col).
// Integration test pattern (mirror ClassFeaturesResolutionIntegrationTests):
//   [Collection("postgres")] ... (PostgresFixture pg): IAsyncLifetime; InitializeAsync => pg.ResetAsync();
//   var classTables = new FivetoolsTableProjection().BuildForBook(TestPaths.RepoFile("5etools"), "PHB");
//   var t = classTables.Single(x => x.Id == "phb14.table.<class>");
//   var file = new CanonicalJsonFile("1", new CanonicalBookMetadata("PHB","Edition2014","x","PHB"), [], new[]{t}, []);
//   await new StructuredFactProjector(dbf).ProjectAsync(file, CancellationToken.None);
//   seed HeroSnapshot(0, heroId, campaignId, name, level, DateTime.UtcNow, sheet); snapId via query;
//   var svc = new CharacterResolutionService(dbf, new HeroRepository(dbf)); var fact = await svc.ResolveAsync(snapId, "<feature>");
```

---

## Task 1: `class resources` feature

**Files:**
- Modify: `Features/Resolution/CharacterResolutionService.cs` (dispatch branch + `ResolveClassResourcesAsync` + a `NonResourceColumns` set)
- Test: `DndMcpAICsharpFun.Tests/Persistence/ClassResourcesResolutionIntegrationTests.cs` (real Postgres — reads a table)

**Interfaces:**
- Produces: `private async Task<ResolvedFact> ResolveClassResourcesAsync(CharacterSheet sheet, CancellationToken ct)`; dispatch on feature `"class resources"`.

- [ ] **Step 1: Write failing integration tests** (mirror `ClassFeaturesResolutionIntegrationTests`; seed the REAL projected tables so the column names are the real ones).
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
public sealed class ClassResourcesResolutionIntegrationTests(PostgresFixture pg) : IAsyncLifetime
{
    public Task InitializeAsync() => pg.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private IDbContextFactory<AppDbContext> DbFactory()
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<AppDbContext>(o => o.UseNpgsql(pg.Container.GetConnectionString()));
        return services.BuildServiceProvider().GetRequiredService<IDbContextFactory<AppDbContext>>();
    }

    private async Task<long> SeedAsync(IDbContextFactory<AppDbContext> dbf, string classSlug, CharacterSheet sheet, long heroId)
    {
        var classTables = new FivetoolsTableProjection().BuildForBook(TestPaths.RepoFile("5etools"), "PHB");
        var table = classTables.Single(t => t.Id == $"phb14.table.{classSlug}");
        var file = new CanonicalJsonFile("1", new CanonicalBookMetadata("PHB", "Edition2014", "x", "PHB"), [], new[] { table }, []);
        await new StructuredFactProjector(dbf).ProjectAsync(file, CancellationToken.None);
        await using var db = pg.NewContext();
        db.HeroSnapshots.Add(new HeroSnapshot(0, heroId, 1, "H", sheet.Level, DateTime.UtcNow, sheet));
        await db.SaveChangesAsync();
        return await db.HeroSnapshots.Where(s => s.HeroId == heroId).Select(s => s.Id).FirstAsync();
    }

    [Fact]
    public async Task Barbarian_rages_resolve_at_level_with_provenance()
    {
        var dbf = DbFactory();
        var sheet = new CharacterSheet { Classes = [new ClassLevel { Class = "Barbarian", Level = 5 }] };
        var snapId = await SeedAsync(dbf, "barbarian", sheet, heroId: 1);

        var fact = await new CharacterResolutionService(dbf, new HeroRepository(dbf)).ResolveAsync(snapId, "class resources");

        fact.Confidence.Should().Be("ok");
        fact.Components.Should().Contain(c => c.Label == "Barbarian Rages" && !string.IsNullOrWhiteSpace(c.Value));
        fact.Components.First(c => c.Label == "Barbarian Rages").Provenance.Should().NotBeNull();
        fact.Components.Should().NotContain(c => c.Label.Contains("Features") || c.Label.Contains("Proficiency Bonus"));
    }

    [Fact]
    public async Task Fighter_with_no_resource_column_resolves_none_not_fabricated()
    {
        var dbf = DbFactory();
        var sheet = new CharacterSheet { Classes = [new ClassLevel { Class = "Fighter", Level = 6 }] };
        var snapId = await SeedAsync(dbf, "fighter", sheet, heroId: 2);

        var fact = await new CharacterResolutionService(dbf, new HeroRepository(dbf)).ResolveAsync(snapId, "class resources");

        fact.Confidence.Should().Be("ok");
        fact.Components.Should().ContainSingle(c => c.Label == "Fighter" && c.Value == "none");
    }

    [Fact]
    public async Task Missing_class_table_is_needsReview()
    {
        var dbf = DbFactory(); // no projection seeded
        var sheet = new CharacterSheet { Classes = [new ClassLevel { Class = "Barbarian", Level = 5 }] };
        await using var db = pg.NewContext();
        db.HeroSnapshots.Add(new HeroSnapshot(0, 3, 1, "H", 5, DateTime.UtcNow, sheet));
        await db.SaveChangesAsync();
        var snapId = await db.HeroSnapshots.Where(s => s.HeroId == 3).Select(s => s.Id).FirstAsync();

        var fact = await new CharacterResolutionService(dbf, new HeroRepository(dbf)).ResolveAsync(snapId, "class resources");
        fact.Confidence.Should().Be("needsReview");
    }
}
```

- [ ] **Step 2: Run → FAIL** (`ResolveForSheetAsync` throws `NotSupportedException` for `"class resources"`).
Run: `dotnet test --filter "FullyQualifiedName~ClassResourcesResolution"` (dangerouslyDisableSandbox). Expected: FAIL (feature not supported).

- [ ] **Step 3: Implement** — Serena `insert_after_symbol` (after `ResolveClassFeaturesAsync`) to add the method + the static set, and `replace_symbol_body` on `ResolveForSheetAsync` to add the branch. Add the dispatch line `if (feature.Equals("class resources", StringComparison.OrdinalIgnoreCase)) return ResolveClassResourcesAsync(sheet, ct);` alongside the others.
```csharp
    private static readonly HashSet<string> NonResourceColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "Level", "Proficiency Bonus", "Features", "Cantrips Known", "Spells Known",
        "Spell Slots", "Slot Level", "1st", "2nd", "3rd", "4th", "5th", "6th", "7th", "8th", "9th",
    };

    private async Task<ResolvedFact> ResolveClassResourcesAsync(CharacterSheet sheet, CancellationToken ct)
    {
        await using var db = await dbf.CreateDbContextAsync(ct);
        var components = new List<ResolvedComponent>();
        var rendered = new List<string>();
        var confidence = "ok";

        foreach (var c in sheet.Classes)
        {
            if (string.IsNullOrWhiteSpace(c.Class)) continue;
            var suffix = $".table.{EntityIdSlug.Slug(c.Class)}";
            var tables = await db.StructuredTables.Where(t => t.CanonicalId.EndsWith(suffix)).Take(2).ToListAsync(ct);
            if (tables.Count == 0)
            {
                confidence = "needsReview";
                components.Add(new ResolvedComponent(c.Class, "[class table not found]", null));
                continue;
            }
            if (tables.Count > 1)
            {
                confidence = "needsReview";
                components.Add(new ResolvedComponent(c.Class, $"[ambiguous: multiple books define {suffix}]", null));
                continue;
            }
            var table = tables[0];
            var columns = JsonSerializer.Deserialize<List<string>>(table.ColumnsJson, JsonOpts) ?? [];
            var row = await db.StructuredTableRows
                .FirstOrDefaultAsync(r => r.TableId == table.Id && r.RowIndex == c.Level - 1, ct);
            if (row is null)
            {
                confidence = "needsReview";
                components.Add(new ResolvedComponent(c.Class, "[no row for level]", null));
                continue;
            }
            var cells = JsonSerializer.Deserialize<List<CanonicalCell>>(row.CellsJson, JsonOpts) ?? [];
            var any = false;
            for (var i = 0; i < columns.Count && i < cells.Count; i++)
            {
                if (NonResourceColumns.Contains(columns[i])) continue;
                if (string.IsNullOrWhiteSpace(cells[i].Value)) continue;
                components.Add(new ResolvedComponent($"{c.Class} {columns[i]}", cells[i].Value, cells[i].Provenance));
                rendered.Add($"{c.Class} {columns[i]}: {cells[i].Value}");
                any = true;
            }
            if (!any)
            {
                components.Add(new ResolvedComponent(c.Class, "none", null));
                rendered.Add($"{c.Class}: none");
            }
        }

        if (components.Count == 0)
            return new ResolvedFact("class resources", "no classes", [], "needsReview");
        return new ResolvedFact("class resources", string.Join(" | ", rendered), components, confidence);
    }
```

- [ ] **Step 4: Run → PASS.** Build 0/0. `dotnet format` the two files.
- [ ] **Step 5: Commit:** `feat(resolution): class resources by level from projected class tables`

---

## Task 2: `saving throws` feature (pure static)

**Files:**
- Create: `Features/Resolution/SavingThrowProficiencies.cs`
- Modify: `Features/Resolution/CharacterResolutionService.cs` (`ResolveSavingThrows` static + dispatch branch)
- Test: `DndMcpAICsharpFun.Tests/Resolution/SavingThrowsResolutionTests.cs` (pure unit — no DB)

**Interfaces:**
- Produces: `SavingThrowProficiencies.For(string @class) : (string, string)?`; `public static ResolvedFact CharacterResolutionService.ResolveSavingThrows(CharacterSheet sheet)`; dispatch on `"saving throws"`.

- [ ] **Step 1: Write failing unit tests** (no DB — pure static).
```csharp
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Resolution;
using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Resolution;

public sealed class SavingThrowsResolutionTests
{
    private static CharacterSheet Sheet(params (string Class, int Level)[] classes)
    {
        var s = new CharacterSheet { Strength = 10, Dexterity = 10, Constitution = 10, Intelligence = 10, Wisdom = 14, Charisma = 10 };
        s.Classes = classes.Select(c => new ClassLevel { Class = c.Class, Level = c.Level }).ToList();
        return s;
    }

    [Fact]
    public void Proficient_save_adds_pb_nonproficient_does_not()
    {
        var fact = CharacterResolutionService.ResolveSavingThrows(Sheet(("Wizard", 4))); // INT/WIS saves, PB +2, WIS 14 (+2)
        fact.Confidence.Should().Be("ok");
        fact.Components.First(c => c.Label == "Wisdom").Value.Should().Be("+4 (proficient)"); // +2 mod + 2 PB
        fact.Components.First(c => c.Label == "Strength").Value.Should().Be("+0"); // 10 -> +0, not proficient
    }

    [Fact]
    public void Multiclass_save_proficiency_comes_only_from_starting_class()
    {
        // Classes[0] = Fighter (STR/CON). Wizard's INT/WIS must NOT be proficient.
        var fact = CharacterResolutionService.ResolveSavingThrows(Sheet(("Fighter", 1), ("Wizard", 1)));
        fact.Components.First(c => c.Label == "Constitution").Value.Should().Contain("proficient");
        fact.Components.First(c => c.Label == "Wisdom").Value.Should().NotContain("proficient");
    }

    [Fact]
    public void Unknown_starting_class_is_needsReview()
    {
        var fact = CharacterResolutionService.ResolveSavingThrows(Sheet(("Homebrewer", 3)));
        fact.Confidence.Should().Be("needsReview");
    }
}
```

- [ ] **Step 2: Run → FAIL** (type/method missing).
Run: `dotnet test --filter "FullyQualifiedName~SavingThrowsResolution"`. Expected: FAIL (does not compile / method missing).

- [ ] **Step 3: Implement** — create `SavingThrowProficiencies.cs`:
```csharp
namespace DndMcpAICsharpFun.Features.Resolution;

/// <summary>
/// PHB proficient saving-throw abilities per class. A character is proficient in exactly these two
/// saves, granted by the STARTING class only (5e grants no save proficiency from multiclassing).
/// </summary>
public static class SavingThrowProficiencies
{
    private static readonly Dictionary<string, (string, string)> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Barbarian"] = ("Strength", "Constitution"),
        ["Bard"] = ("Dexterity", "Charisma"),
        ["Cleric"] = ("Wisdom", "Charisma"),
        ["Druid"] = ("Intelligence", "Wisdom"),
        ["Fighter"] = ("Strength", "Constitution"),
        ["Monk"] = ("Strength", "Dexterity"),
        ["Paladin"] = ("Wisdom", "Charisma"),
        ["Ranger"] = ("Strength", "Dexterity"),
        ["Rogue"] = ("Dexterity", "Intelligence"),
        ["Sorcerer"] = ("Constitution", "Charisma"),
        ["Warlock"] = ("Wisdom", "Charisma"),
        ["Wizard"] = ("Intelligence", "Wisdom"),
    };

    /// <summary>The two proficient save abilities for a class, or null for an unknown/homebrew class.</summary>
    public static (string, string)? For(string @class) => Map.TryGetValue(@class, out var v) ? v : null;
}
```
Then Serena-add the static method + dispatch branch (`if (feature.Equals("saving throws", StringComparison.OrdinalIgnoreCase)) return Task.FromResult(ResolveSavingThrows(sheet));`):
```csharp
    /// <summary>
    /// Per-ability saving-throw bonus = ability modifier + (proficiency bonus if the STARTING class is
    /// proficient in that save). Pure/computed — no DB, no provenance. Unknown starting class => needsReview.
    /// </summary>
    public static ResolvedFact ResolveSavingThrows(CharacterSheet sheet)
    {
        var starting = sheet.Classes.Count > 0 ? sheet.Classes[0].Class : "";
        var profs = SavingThrowProficiencies.For(starting);
        if (profs is null)
            return new ResolvedFact("saving throws", "unknown class", [], "needsReview");

        var (p1, p2) = profs.Value;
        var pb = sheet.ProficiencyBonus;
        var abilities = new (string Name, int Score)[]
        {
            ("Strength", sheet.Strength), ("Dexterity", sheet.Dexterity), ("Constitution", sheet.Constitution),
            ("Intelligence", sheet.Intelligence), ("Wisdom", sheet.Wisdom), ("Charisma", sheet.Charisma),
        };
        var components = new List<ResolvedComponent>();
        var rendered = new List<string>();
        foreach (var (name, score) in abilities)
        {
            var proficient = string.Equals(name, p1, StringComparison.Ordinal) || string.Equals(name, p2, StringComparison.Ordinal);
            var bonus = CharacterSheet.Modifier(score) + (proficient ? pb : 0);
            var val = (bonus >= 0 ? $"+{bonus}" : bonus.ToString()) + (proficient ? " (proficient)" : "");
            components.Add(new ResolvedComponent(name, val, null));
            rendered.Add($"{name} {val}");
        }
        return new ResolvedFact("saving throws", string.Join(", ", rendered), components, "ok");
    }
```

- [ ] **Step 4: Run → PASS.** Build 0/0. Format both files.
- [ ] **Step 5: Commit:** `feat(resolution): saving throws from class save-proficiency map + computed modifiers`

---

## Task 3: `spell count` feature (branch-per-caster-type)

**Files:**
- Modify: `Features/Resolution/CharacterResolutionService.cs` (`ResolveSpellCountAsync` + `ModForAbility` helper + dispatch branch)
- Test: `DndMcpAICsharpFun.Tests/Persistence/SpellCountResolutionIntegrationTests.cs` (real Postgres — known-caster reads a table; also covers the computed branches so all are seen against real data)

**Interfaces:**
- Produces: `private async Task<ResolvedFact> ResolveSpellCountAsync(CharacterSheet sheet, CancellationToken ct)`; dispatch on `"spell count"`. Classification is DATA-DRIVEN: known ⇔ table row has a `Spells Known` cell; prepared ⇔ no such cell but `SpellcastingAbility != null` (formula by `Classify` full/half); non-caster ⇔ `SpellcastingAbility == null`.

- [ ] **Step 1: Write failing integration tests — ONE PER BRANCH.** Seed the REAL projected tables (bard=known, cleric=prepared-full, paladin=prepared-half, fighter=non-caster) via the same `SeedAsync` helper shape as Task 1.
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
public sealed class SpellCountResolutionIntegrationTests(PostgresFixture pg) : IAsyncLifetime
{
    public Task InitializeAsync() => pg.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private IDbContextFactory<AppDbContext> DbFactory()
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<AppDbContext>(o => o.UseNpgsql(pg.Container.GetConnectionString()));
        return services.BuildServiceProvider().GetRequiredService<IDbContextFactory<AppDbContext>>();
    }

    private async Task<long> SeedAsync(IDbContextFactory<AppDbContext> dbf, string classSlug, CharacterSheet sheet, long heroId)
    {
        var classTables = new FivetoolsTableProjection().BuildForBook(TestPaths.RepoFile("5etools"), "PHB");
        var table = classTables.Single(t => t.Id == $"phb14.table.{classSlug}");
        var file = new CanonicalJsonFile("1", new CanonicalBookMetadata("PHB", "Edition2014", "x", "PHB"), [], new[] { table }, []);
        await new StructuredFactProjector(dbf).ProjectAsync(file, CancellationToken.None);
        await using var db = pg.NewContext();
        db.HeroSnapshots.Add(new HeroSnapshot(0, heroId, 1, "H", sheet.Level, DateTime.UtcNow, sheet));
        await db.SaveChangesAsync();
        return await db.HeroSnapshots.Where(s => s.HeroId == heroId).Select(s => s.Id).FirstAsync();
    }

    private static CharacterSheet Sheet(string @class, int level, (string Ability, int Score) primary)
    {
        var s = new CharacterSheet { Classes = [new ClassLevel { Class = @class, Level = level }] };
        typeof(CharacterSheet).GetProperty(primary.Ability)!.SetValue(s, primary.Score);
        return s;
    }

    [Fact] // KNOWN caster: reads Spells Known cell with provenance
    public async Task Known_caster_reads_spells_known_from_table()
    {
        var dbf = DbFactory();
        var snapId = await SeedAsync(dbf, "bard", Sheet("Bard", 3, ("Charisma", 16)), heroId: 1);
        var fact = await new CharacterResolutionService(dbf, new HeroRepository(dbf)).ResolveAsync(snapId, "spell count");
        fact.Confidence.Should().Be("ok");
        var bard = fact.Components.First(c => c.Label == "Bard");
        bard.Value.Should().Contain("known");
        bard.Provenance.Should().NotBeNull(); // table-sourced
    }

    [Fact] // PREPARED full caster: mod + level, no provenance
    public async Task Prepared_full_caster_computes_mod_plus_level()
    {
        var dbf = DbFactory();
        var snapId = await SeedAsync(dbf, "cleric", Sheet("Cleric", 5, ("Wisdom", 16)), heroId: 2); // +3 mod
        var fact = await new CharacterResolutionService(dbf, new HeroRepository(dbf)).ResolveAsync(snapId, "spell count");
        var cleric = fact.Components.First(c => c.Label == "Cleric");
        cleric.Value.Should().Contain("8 prepared"); // 3 + 5
        cleric.Provenance.Should().BeNull(); // computed
    }

    [Fact] // PREPARED half caster (Paladin): mod + level/2, min 1
    public async Task Prepared_half_caster_uses_half_level()
    {
        var dbf = DbFactory();
        var snapId = await SeedAsync(dbf, "paladin", Sheet("Paladin", 6, ("Charisma", 14)), heroId: 3); // +2 mod
        var fact = await new CharacterResolutionService(dbf, new HeroRepository(dbf)).ResolveAsync(snapId, "spell count");
        fact.Components.First(c => c.Label == "Paladin").Value.Should().Contain("5 prepared"); // 2 + 6/2 = 5
    }

    [Fact] // NON-caster: no fabricated number
    public async Task Non_caster_contributes_no_spellcasting()
    {
        var dbf = DbFactory();
        var snapId = await SeedAsync(dbf, "fighter", Sheet("Fighter", 3, ("Strength", 16)), heroId: 4);
        var fact = await new CharacterResolutionService(dbf, new HeroRepository(dbf)).ResolveAsync(snapId, "spell count");
        fact.Components.First(c => c.Label == "Fighter").Value.Should().Be("no spellcasting");
    }
}
```

- [ ] **Step 2: Run → FAIL** (`"spell count"` not supported).
Run: `dotnet test --filter "FullyQualifiedName~SpellCountResolution"`. Expected: FAIL.

- [ ] **Step 3: Implement** — Serena-add the method + helper after `ResolveClassResourcesAsync`, and the dispatch branch (`if (feature.Equals("spell count", StringComparison.OrdinalIgnoreCase)) return ResolveSpellCountAsync(sheet, ct);`).
```csharp
    private static int ModForAbility(CharacterSheet s, string ability) => CharacterSheet.Modifier(ability switch
    {
        "Strength" => s.Strength, "Dexterity" => s.Dexterity, "Constitution" => s.Constitution,
        "Intelligence" => s.Intelligence, "Wisdom" => s.Wisdom, "Charisma" => s.Charisma, _ => 10,
    });

    private async Task<ResolvedFact> ResolveSpellCountAsync(CharacterSheet sheet, CancellationToken ct)
    {
        await using var db = await dbf.CreateDbContextAsync(ct);
        var components = new List<ResolvedComponent>();
        var rendered = new List<string>();
        var confidence = "ok";
        var contributed = false;

        foreach (var c in sheet.Classes)
        {
            if (string.IsNullOrWhiteSpace(c.Class)) continue;

            // Load the class table row (needed for the known-caster read + cantrips).
            var suffix = $".table.{EntityIdSlug.Slug(c.Class)}";
            var tables = await db.StructuredTables.Where(t => t.CanonicalId.EndsWith(suffix)).Take(2).ToListAsync(ct);
            var ambiguous = tables.Count > 1;
            var table = tables.Count == 1 ? tables[0] : null;

            List<string> columns = [];
            List<CanonicalCell> cells = [];
            if (table is not null)
            {
                columns = JsonSerializer.Deserialize<List<string>>(table.ColumnsJson, JsonOpts) ?? [];
                var row = await db.StructuredTableRows
                    .FirstOrDefaultAsync(r => r.TableId == table.Id && r.RowIndex == c.Level - 1, ct);
                cells = row is null ? [] : JsonSerializer.Deserialize<List<CanonicalCell>>(row.CellsJson, JsonOpts) ?? [];
            }

            CanonicalCell? Cell(string col)
            {
                for (var i = 0; i < columns.Count && i < cells.Count; i++)
                    if (string.Equals(columns[i], col, StringComparison.OrdinalIgnoreCase)) return cells[i];
                return null;
            }

            var known = Cell("Spells Known");
            var cantrips = Cell("Cantrips Known");
            var ability = MulticlassSpellcasting.SpellcastingAbility(c.Class);

            List<string> Parts(string head) =>
                cantrips is not null && !string.IsNullOrWhiteSpace(cantrips.Value)
                    ? [head, $"{cantrips.Value} cantrips"] : [head];

            if (known is not null && !string.IsNullOrWhiteSpace(known.Value))
            {
                var val = string.Join(", ", Parts($"{known.Value} known"));
                components.Add(new ResolvedComponent(c.Class, val, known.Provenance));
                rendered.Add($"{c.Class}: {val}");
                contributed = true;
            }
            else if (ambiguous)
            {
                confidence = "needsReview";
                components.Add(new ResolvedComponent(c.Class, $"[ambiguous: multiple books define {suffix}]", null));
            }
            else if (ability is not null)
            {
                var mod = ModForAbility(sheet, ability);
                var prepared = MulticlassSpellcasting.Classify(c) == CasterType.Half ? mod + c.Level / 2 : mod + c.Level;
                prepared = Math.Max(1, prepared);
                var val = string.Join(", ", Parts($"{prepared} prepared"));
                components.Add(new ResolvedComponent(c.Class, val, null));
                rendered.Add($"{c.Class}: {val}");
                contributed = true;
            }
            else
            {
                components.Add(new ResolvedComponent(c.Class, "no spellcasting", null));
                rendered.Add($"{c.Class}: no spellcasting");
            }
        }

        if (!contributed)
            return new ResolvedFact("spell count",
                components.Count > 0 ? string.Join(" | ", rendered) : "no classes", components, "needsReview");
        return new ResolvedFact("spell count", string.Join(" | ", rendered), components, confidence);
    }
```

- [ ] **Step 4: Run → PASS** (all four branch tests). Build 0/0. Format both files.
- [ ] **Step 5: Commit:** `feat(resolution): spell count — known (table) + prepared (formula) per caster type`

---

## Task 4: Gates
- [ ] **Step 1:** `dotnet build` 0/0; FULL `dotnet test` green (Docker up for the integration tests; if Docker is unavailable run `--filter "FullyQualifiedName!~Persistence"` and NOTE the integration tests were not run — but they are the grounding proof, so run them when Docker is available). `dotnet format DndMcpAICsharpFun.slnx --include` the touched files; `git diff --stat` confined to `Features/Resolution/*` + the three test files; `.http`/insomnia untouched.

---

## Self-Review notes
- Spec "class resources" (Req 1) → Task 1 (`NonResourceColumns` exclusion, per-cell provenance, `none`/`needsReview`). "saving throws" (Req 2) → Task 2 (static map, `Classes[0]` only, computed). "spell count" (Req 3) → Task 3 (data-driven known/prepared/non-caster; full vs half formula). Grounding proven on REAL projected column names by the Task 1 & 3 integration tests (they cover the tasks.md "real-infra grounding" item inherently).
- Branch-per-classifier discipline: Task 3 has one test PER branch (known / prepared-full / prepared-half / non-caster). Task 2 tests proficient/non-proficient/multiclass/unknown.
- Reuse: table-lookup shape + ambiguity guard from `ResolveClassFeaturesAsync`; `SpellcastingAbility`/`Classify`; `CharacterSheet.Modifier`/`ProficiencyBonus`; `EntityIdSlug.Slug`. Only new data = the save map.
- No endpoint/schema change; AC deferred. All resolver methods return honest `needsReview`/`none`, never a fabricated value.
