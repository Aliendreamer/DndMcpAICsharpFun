# Multiclass Character Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extend `CharacterSheet` to a per-class level model (any combination, caster or not), add deterministic multiclass validity and combined-caster-level spellcasting, and expose the single-vs-multi resolution fork as cited MCP tools.

**Architecture:** `CharacterSheet.Classes: List<ClassLevel>` becomes the source of truth; `Class`/`Subclass`/`Level`/`ProficiencyBonus` become derived `[JsonIgnore]` getters. Legacy single-class `HeroSnapshot` JSON (flat `Class`/`Level`, no `Classes`) is migrated tolerantly at deserialize time via private set-only sink properties (`[JsonPropertyName]`+`[JsonInclude]`) feeding the STJ `IJsonOnDeserialized` hook — no data-migration script. (Set-only sinks, not `[JsonExtensionData]`: the `[JsonIgnore]` derived getters shadow the legacy keys from any extension-data bag — verified empirically.) Multiclass validity (`MulticlassRules`) and spellcasting composition (`MulticlassSpellcasting`) are pure deterministic code; the combined-level→slots reference table is seeded into Postgres `StructuredTables` so the resolved answer carries a `ProvenanceRef`, exactly as slice-1's breath-weapon path does. `CharacterResolutionService` gains a fork keyed on `Classes`, surfaced through the existing per-user in-process MCP tool plus a new `check_multiclass` tool.

**Tech Stack:** .NET 10, C# (nullable + warnings-as-errors), System.Text.Json, EF Core (Npgsql/PostgreSQL), xunit + FluentAssertions + NSubstitute (global usings). Persistence tests run against real Postgres via Testcontainers + Respawn (Docker required).

## Global Constraints

- **Target:** `net10.0`; nullable enabled; **warnings-as-errors** for every project (`Directory.Build.props`) — a warning fails the build.
- **Source-of-truth rule:** `Classes` is authoritative; `Class`/`Subclass`/`Level`/`ProficiencyBonus` are DERIVED and never independently stored. Any code that "sets a class" goes through `CharacterSheet.SetSingleClass(...)`.
- **Provenance rule:** a computed value (e.g. save DC, combined caster level) carries NO provenance rather than mis-citing a source (slice-1 COR-20). A value read from a seeded table cell carries that cell's `Provenance`.
- **Warlock rule:** Warlock Pact Magic is NEVER merged into the combined caster-level pool; it is always a separate component.
- **Security rule (SEC-08):** character-scoped resolution stays off the shared-key MCP surface; it is added per-request in `DndChatService`, closing over the signed-in `userId`, always through `ResolveForUserAsync` (server-side ownership check).
- **No new HTTP routes** are introduced (resolution tools are in-process `AIFunction`s, not `MapGet`/`MapPost`). If that changes, update `DndMcpAICsharpFun.http` AND `dnd-mcp-api.insomnia.json` in the same commit.
- **Test data:** persistence round-trip tests use the existing `AppDbContext` JSON column (`HeroSnapshot.CharacterSheet`, `JsonSerializer.Serialize(v, (JsonSerializerOptions?)null)`), i.e. default `JsonSerializerOptions`.
- **Serena rule (project):** use Serena symbolic tools for code reads/edits on existing files; the built-in Read/Edit are not for code files here.

---

## File Structure

- `Domain/CharacterSheet.cs` — **modify**: add `ClassLevel`; add `Classes` list; convert `Class`/`Subclass`/`Level`/`ProficiencyBonus` to derived `[JsonIgnore]` getters; add private set-only legacy sinks (`[JsonPropertyName]`+`[JsonInclude]`); implement `IJsonOnDeserialized`; add `SetSingleClass`.
- `Features/Resolution/MulticlassRules.cs` — **create**: ability-score prerequisite map + reduced proficiency-subset map (all combos, caster or not).
- `Features/Resolution/MulticlassSpellcasting.cs` — **create**: caster-type classification, combined caster level, Warlock pact, per-class spellcasting ability.
- `Features/Resolution/MulticlassSlotTableSeeder.cs` — **create**: idempotent seeding of the `phb14.table.multiclass-spellcaster` `StructuredTable` (+ 20 rows, PHB provenance).
- `Extensions/WebApplicationExtensions.cs` — **modify**: invoke the seeder after `MigrateDatabaseAsync`.
- `Features/Resolution/CharacterResolutionService.cs` — **modify**: fork `ResolveForSheetAsync` to add `spell slots` and `spell save dc` features; add `ResolveMulticlassValidityAsync` (target-class param).
- `Features/Chat/DndChatService.cs` — **modify**: add the `check_multiclass` per-user in-process tool alongside `resolve_character_feature`.
- `DndMcpAICsharpFun.Tests/...` — **create/modify**: unit tests per task; one persistence round-trip test for the legacy-migration + new-write path.

---

## Task 1: Per-class level model + derived flat fields

**Files:**
- Modify: `Domain/CharacterSheet.cs`
- Test: `DndMcpAICsharpFun.Tests/Domain/CharacterSheetClassesTests.cs` (create)

**Interfaces:**
- Produces:
  - `public sealed class ClassLevel { public string Class {get;set;}; public int Level {get;set;}; public string Subclass {get;set;} }`
  - `CharacterSheet.Classes : List<ClassLevel>` (source of truth)
  - derived `CharacterSheet.Class : string` (=`Classes[0].Class` or `""`), `.Subclass : string`, `.Level : int` (=Σ), `.ProficiencyBonus : int` (=`2 + (max(1,Level)-1)/4`)
  - `CharacterSheet.SetSingleClass(string @class, string subclass, int level) : void`

- [ ] **Step 1: Write the failing test — derived total level & proficiency bonus**

```csharp
// DndMcpAICsharpFun.Tests/Domain/CharacterSheetClassesTests.cs
using DndMcpAICsharpFun.Domain;

namespace DndMcpAICsharpFun.Tests.Domain;

public sealed class CharacterSheetClassesTests
{
    [Fact]
    public void Multiclass_derives_total_level_primary_class_and_proficiency_bonus()
    {
        var sheet = new CharacterSheet
        {
            Classes =
            [
                new ClassLevel { Class = "Rogue", Level = 3, Subclass = "Thief" },
                new ClassLevel { Class = "Fighter", Level = 2, Subclass = "" },
            ],
        };

        sheet.Level.Should().Be(5);              // total level = Σ per-class
        sheet.Class.Should().Be("Rogue");        // primary = Classes[0]
        sheet.Subclass.Should().Be("Thief");
        sheet.ProficiencyBonus.Should().Be(3);   // 5th-level PB
    }

    [Fact]
    public void SetSingleClass_replaces_the_class_list_with_one_entry()
    {
        var sheet = new CharacterSheet();
        sheet.SetSingleClass("Wizard", "Evocation", 5);

        sheet.Classes.Should().ContainSingle();
        sheet.Class.Should().Be("Wizard");
        sheet.Subclass.Should().Be("Evocation");
        sheet.Level.Should().Be(5);
    }

    [Fact]
    public void Empty_sheet_has_safe_defaults()
    {
        var sheet = new CharacterSheet();
        sheet.Class.Should().Be("");
        sheet.Level.Should().Be(0);
        sheet.ProficiencyBonus.Should().Be(2); // floor: level treated as >=1
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~CharacterSheetClassesTests`
Expected: FAIL — `ClassLevel` does not exist / `Classes` not defined / `Class` has a setter but no derivation.

- [ ] **Step 3: Modify `CharacterSheet.cs`**

Add the `using` at the top of the file (for `IJsonOnDeserialized`, `[JsonIgnore]`, `[JsonInclude]`, `[JsonPropertyName]`):

```csharp
using System.Text.Json.Serialization;
```

Add the `ClassLevel` type (below `CharacterFeature`):

```csharp
public sealed class ClassLevel
{
    public string Class { get; set; } = "";
    public int Level { get; set; }
    public string Subclass { get; set; } = "";
}
```

In `CharacterSheet`, make the class implement the STJ post-deserialize hook:

```csharp
public sealed class CharacterSheet : IJsonOnDeserialized
```

Replace the flat `Class`/`Subclass`/`Level` auto-properties (lines 6-9) with the source-of-truth list plus derived getters. Keep `Race`/`Background`/etc. as-is:

```csharp
    public string Race { get; set; } = "";

    /// <summary>Source of truth for the character's class(es). One entry per class taken.</summary>
    public List<ClassLevel> Classes { get; set; } = [];

    [JsonIgnore] public string Class => Classes.Count > 0 ? Classes[0].Class : "";
    [JsonIgnore] public string Subclass => Classes.Count > 0 ? Classes[0].Subclass : "";
    [JsonIgnore] public int Level => Classes.Sum(c => c.Level);

    public string Background { get; set; } = "";
```

Delete the old `public int Level { get; set; }` line. Then convert the stored `ProficiencyBonus` (line 25) to a derived getter — replace `public int ProficiencyBonus { get; set; }` with:

```csharp
    [JsonIgnore] public int ProficiencyBonus => 2 + (Math.Max(1, Level) - 1) / 4;
```

Add, at the end of the class body (after `ModifierStr`), the legacy deserialization sinks, the migration hook, and the helper.

**Why set-only sinks, not `[JsonExtensionData]`:** the derived `[JsonIgnore]` getters named `Class`/`Subclass`/`Level` **shadow** the legacy JSON keys — an ignored member still "claims" its name, so STJ matches the incoming `"Class"` key to the ignored getter and drops it; it never reaches a `[JsonExtensionData]` bag (verified empirically: extension-data stays empty). Instead, capture each legacy key with a private **set-only** property carrying `[JsonPropertyName(...)]` + `[JsonInclude]`. Set-only (no getter) means STJ deserializes into it but never re-serializes it, so migrated sheets round-trip as `{"Classes":[...]}` with no stray top-level `"Class"`/`"Level"` keys. No name collision occurs because the public same-named getters are `[JsonIgnore]`.

```csharp
    // Legacy deserialization sinks: pre-multiclass HeroSnapshot rows wrote flat "Class"/"Subclass"/"Level".
    // The [JsonIgnore] derived getters above shadow those keys from [JsonExtensionData], so capture them
    // here instead. Set-only (no getter) => deserialized but never re-serialized. Consumed in OnDeserialized.
    private string? _legacyClass;
    private string? _legacySubclass;
    private int? _legacyLevel;

    [JsonInclude, JsonPropertyName("Class")]
    internal string LegacyClassSink { set => _legacyClass = value; }
    [JsonInclude, JsonPropertyName("Subclass")]
    internal string LegacySubclassSink { set => _legacySubclass = value; }
    [JsonInclude, JsonPropertyName("Level")]
    internal int LegacyLevelSink { set => _legacyLevel = value; }

    void IJsonOnDeserialized.OnDeserialized()
    {
        // Tolerant migration: a legacy single-class snapshot has flat "Class"/"Level" and no "Classes".
        // Back-fill a one-entry list. Fires only when Classes is empty AND a flat class name was present,
        // so a genuinely class-less sheet stays empty.
        if (Classes.Count == 0 && !string.IsNullOrWhiteSpace(_legacyClass))
            Classes.Add(new ClassLevel
            {
                Class = _legacyClass,
                Subclass = _legacySubclass ?? "",
                Level = _legacyLevel ?? 1,
            });
    }

    /// <summary>Sets the character to a single class (the common non-multiclass path).</summary>
    public void SetSingleClass(string @class, string subclass, int level)
        => Classes = [new ClassLevel { Class = @class, Subclass = subclass, Level = level }];
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~CharacterSheetClassesTests`
Expected: PASS (3 tests).

- [ ] **Step 5: Fix all writers of the (now read-only) flat fields**

Run: `grep -rnE '\.Level\s*=|\.Class\s*=|\.Subclass\s*=|\.ProficiencyBonus\s*=' Features CompanionUI Domain DndMcpAICsharpFun.Tests --include=*.cs | grep -viE 'ClassLevel|\.Level\)|==|SpellLevel'`
For each site that assigns a `CharacterSheet`'s flat `Class`/`Subclass`/`Level`/`ProficiencyBonus`, replace the assignment(s) with a single `sheet.SetSingleClass(class, subclass, level)` call (or set `Classes` directly for a multiclass fixture). Assignments to a `ClassLevel`'s own `Class`/`Level`/`Subclass` are fine and stay.

- [ ] **Step 6: Build to catch the rest**

Run: `dotnet build`
Expected: 0 warnings / 0 errors. (Warnings-as-errors surfaces any remaining assignment to a read-only property as CS0200.)

- [ ] **Step 7: Commit**

```bash
git add Domain/CharacterSheet.cs DndMcpAICsharpFun.Tests/Domain/CharacterSheetClassesTests.cs
git commit -m "feat(character): per-class level model with derived flat fields"
```

---

## Task 2: Tolerant legacy-JSON migration (round-trip)

**Files:**
- Test: `DndMcpAICsharpFun.Tests/Domain/CharacterSheetMigrationTests.cs` (create)

**Interfaces:**
- Consumes: `CharacterSheet` (Task 1), `IJsonOnDeserialized` hook, `SetSingleClass`.

- [ ] **Step 1: Write the failing test — legacy flat JSON migrates; round-trip is stable**

```csharp
// DndMcpAICsharpFun.Tests/Domain/CharacterSheetMigrationTests.cs
using System.Text.Json;
using DndMcpAICsharpFun.Domain;

namespace DndMcpAICsharpFun.Tests.Domain;

public sealed class CharacterSheetMigrationTests
{
    [Fact]
    public void Legacy_flat_snapshot_loads_as_one_class_character()
    {
        // A pre-multiclass row: flat Class/Level, no "Classes" array.
        const string legacy = """
        { "Race": "Human", "Class": "Wizard", "Subclass": "Evocation", "Level": 5,
          "Constitution": 14 }
        """;

        var sheet = JsonSerializer.Deserialize<CharacterSheet>(legacy)!;

        sheet.Classes.Should().ContainSingle();
        sheet.Class.Should().Be("Wizard");
        sheet.Subclass.Should().Be("Evocation");
        sheet.Level.Should().Be(5);
    }

    [Fact]
    public void Round_trip_of_a_migrated_sheet_is_stable_and_drops_legacy_keys()
    {
        const string legacy = """{ "Class": "Wizard", "Level": 5 }""";

        var first = JsonSerializer.Deserialize<CharacterSheet>(legacy)!;
        var reserialized = JsonSerializer.Serialize(first);
        var second = JsonSerializer.Deserialize<CharacterSheet>(reserialized)!;

        second.Class.Should().Be("Wizard");
        second.Level.Should().Be(5);
        second.Classes.Should().ContainSingle();
        // Legacy top-level "Class"/"Level"/"Subclass" keys are not echoed back — only "Classes" is
        // written (the nested "Level"/"Class" inside each ClassLevel entry is expected and correct,
        // so assert on the ROOT object's properties, not a substring).
        using var doc = JsonDocument.Parse(reserialized);
        doc.RootElement.TryGetProperty("Classes", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("Level", out _).Should().BeFalse();
        doc.RootElement.TryGetProperty("Class", out _).Should().BeFalse();
    }

    [Fact]
    public void Sheet_with_neither_classes_nor_flat_class_stays_empty()
    {
        const string bare = """{ "Race": "Elf" }""";
        var sheet = JsonSerializer.Deserialize<CharacterSheet>(bare)!;
        sheet.Classes.Should().BeEmpty();
        sheet.Class.Should().Be("");
        sheet.Level.Should().Be(0);
    }
}
```

- [ ] **Step 2: Run test to verify it fails or passes**

Run: `dotnet test --filter FullyQualifiedName~CharacterSheetMigrationTests`
Expected: PASS if Task 1's hook is correct. If any fail, fix `OnDeserialized` / the legacy sinks in `CharacterSheet.cs` (do NOT weaken the test). Common cause: a sink that isn't set-only or lacks `[JsonInclude]`+`[JsonPropertyName]`, so the legacy key is dropped and `Classes` stays empty.

- [ ] **Step 3: Commit**

```bash
git add DndMcpAICsharpFun.Tests/Domain/CharacterSheetMigrationTests.cs
git commit -m "test(character): tolerant legacy single-class JSON migration"
```

---

## Task 3: HeroSnapshot persistence round-trip (real Postgres)

**Files:**
- Test: `DndMcpAICsharpFun.Tests/Persistence/HeroSnapshotMulticlassRoundTripTests.cs` (create — follow the existing persistence-test base/fixture in `DndMcpAICsharpFun.Tests/Persistence/`)

**Interfaces:**
- Consumes: `AppDbContext` JSON column for `HeroSnapshot.CharacterSheet` (default `JsonSerializerOptions`), the existing Testcontainers Postgres fixture + Respawn reset.

- [ ] **Step 1: Inspect an existing persistence test to copy the fixture wiring**

Run: `ls DndMcpAICsharpFun.Tests/Persistence` and open one existing test (e.g. a `HeroRepository` test) to copy its `[Collection(...)]` / fixture ctor pattern and how it obtains an `IDbContextFactory<AppDbContext>` or repository.

- [ ] **Step 2: Write the failing test — a multiclass sheet survives a DB round-trip**

```csharp
// DndMcpAICsharpFun.Tests/Persistence/HeroSnapshotMulticlassRoundTripTests.cs
// NOTE: mirror the [Collection]/fixture attributes of the sibling persistence tests in this folder.
using DndMcpAICsharpFun.Domain;

namespace DndMcpAICsharpFun.Tests.Persistence;

public sealed class HeroSnapshotMulticlassRoundTripTests /* : <existing persistence base> */
{
    // ctor: capture the shared Postgres fixture / IDbContextFactory<AppDbContext> like the siblings do.

    [Fact]
    public async Task Multiclass_sheet_persists_and_reloads_with_all_classes()
    {
        var sheet = new CharacterSheet
        {
            Race = "Half-Elf",
            Classes =
            [
                new ClassLevel { Class = "Paladin", Level = 6, Subclass = "Devotion" },
                new ClassLevel { Class = "Sorcerer", Level = 2, Subclass = "Draconic" },
            ],
            Constitution = 14,
        };

        // Persist a HeroSnapshot carrying this sheet, then reload it by id via a fresh context.
        // (Use the same HeroRepository/context path the sibling tests use to save + fetch a snapshot.)
        // long id = await SaveSnapshotAsync(sheet);
        // var reloaded = await GetSnapshotAsync(id);

        // reloaded.Sheet.Classes.Should().HaveCount(2);
        // reloaded.Sheet.Level.Should().Be(8);
        // reloaded.Sheet.Class.Should().Be("Paladin");
    }
}
```

Fill in the commented lines using the concrete save/fetch API the sibling tests use (`HeroRepository.SaveSnapshotAsync` / `GetSnapshotAsync` or equivalent). Do not invent a new persistence API — reuse what exists.

- [ ] **Step 3: Run test to verify it passes (Docker must be running)**

Run: `dotnet test --filter FullyQualifiedName~HeroSnapshotMulticlassRoundTripTests`
Expected: PASS. If Docker is not running, note it and defer — this is the only Docker-dependent task.

- [ ] **Step 4: Commit**

```bash
git add DndMcpAICsharpFun.Tests/Persistence/HeroSnapshotMulticlassRoundTripTests.cs
git commit -m "test(persistence): multiclass HeroSnapshot JSON round-trip"
```

---

## Task 4: Multiclass validity — prerequisites + proficiency subsets

**Files:**
- Create: `Features/Resolution/MulticlassRules.cs`
- Test: `DndMcpAICsharpFun.Tests/Resolution/MulticlassRulesTests.cs` (create)

**Interfaces:**
- Produces:
  - `MulticlassRules.PrereqResult(bool Allowed, string Reason)` (record)
  - `MulticlassRules.CanMulticlassInto(string @class, CharacterSheet sheet) : PrereqResult`
  - `MulticlassRules.MulticlassProficiencies(string @class) : IReadOnlyList<string>`

- [ ] **Step 1: Write the failing test**

```csharp
// DndMcpAICsharpFun.Tests/Resolution/MulticlassRulesTests.cs
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Resolution;

namespace DndMcpAICsharpFun.Tests.Resolution;

public sealed class MulticlassRulesTests
{
    private static CharacterSheet Sheet(int str = 10, int dex = 10, int con = 10,
        int intel = 10, int wis = 10, int cha = 10) =>
        new() { Strength = str, Dexterity = dex, Constitution = con,
                Intelligence = intel, Wisdom = wis, Charisma = cha };

    [Fact]
    public void Rogue_requires_dex_13()
    {
        MulticlassRules.CanMulticlassInto("Rogue", Sheet(dex: 12)).Allowed.Should().BeFalse();
        MulticlassRules.CanMulticlassInto("Rogue", Sheet(dex: 13)).Allowed.Should().BeTrue();
    }

    [Fact]
    public void Fighter_accepts_str_or_dex_13()
    {
        MulticlassRules.CanMulticlassInto("Fighter", Sheet(str: 13, dex: 8)).Allowed.Should().BeTrue();
        MulticlassRules.CanMulticlassInto("Fighter", Sheet(str: 8, dex: 13)).Allowed.Should().BeTrue();
        MulticlassRules.CanMulticlassInto("Fighter", Sheet(str: 8, dex: 8)).Allowed.Should().BeFalse();
    }

    [Fact]
    public void Paladin_requires_str_and_cha_13()
    {
        MulticlassRules.CanMulticlassInto("Paladin", Sheet(str: 13, cha: 12)).Allowed.Should().BeFalse();
        MulticlassRules.CanMulticlassInto("Paladin", Sheet(str: 13, cha: 13)).Allowed.Should().BeTrue();
    }

    [Fact]
    public void Failed_prerequisite_reports_the_reason()
    {
        var r = MulticlassRules.CanMulticlassInto("Rogue", Sheet(dex: 12));
        r.Reason.Should().Contain("Dexterity 13");
    }

    [Fact]
    public void Fighter_multiclass_proficiency_subset_excludes_heavy_armor_and_saves()
    {
        var profs = MulticlassRules.MulticlassProficiencies("Fighter");
        profs.Should().Contain("light armor");
        profs.Should().Contain("medium armor");
        profs.Should().Contain("shields");
        profs.Should().Contain("martial weapons");
        profs.Should().NotContain("heavy armor");
        profs.Should().NotContain(p => p.Contains("saving throw"));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~MulticlassRulesTests`
Expected: FAIL — `MulticlassRules` does not exist.

- [ ] **Step 3: Implement `MulticlassRules.cs`**

```csharp
using DndMcpAICsharpFun.Domain;

namespace DndMcpAICsharpFun.Features.Resolution;

/// <summary>
/// Deterministic multiclass rules for ANY class combination (caster or not): the ability-score
/// prerequisites to take a class as a multiclass, and the reduced proficiency subset it grants
/// (PHB "Multiclassing" — Proficiencies). No spellcasting is involved here.
/// </summary>
public static class MulticlassRules
{
    public sealed record PrereqResult(bool Allowed, string Reason);

    // Which ability scores (>=13) a class demands to multiclass into it. A class with alternatives
    // (Fighter: STR or DEX) is a list of any-of groups; each group is a set of all-of requirements.
    private static readonly Dictionary<string, (string Ability, Func<CharacterSheet, int> Score)[][]> Prereqs =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Barbarian"] = [[("Strength", s => s.Strength)]],
            ["Bard"]      = [[("Charisma", s => s.Charisma)]],
            ["Cleric"]    = [[("Wisdom", s => s.Wisdom)]],
            ["Druid"]     = [[("Wisdom", s => s.Wisdom)]],
            ["Fighter"]   = [[("Strength", s => s.Strength)], [("Dexterity", s => s.Dexterity)]],
            ["Monk"]      = [[("Dexterity", s => s.Dexterity), ("Wisdom", s => s.Wisdom)]],
            ["Paladin"]   = [[("Strength", s => s.Strength), ("Charisma", s => s.Charisma)]],
            ["Ranger"]    = [[("Dexterity", s => s.Dexterity), ("Wisdom", s => s.Wisdom)]],
            ["Rogue"]     = [[("Dexterity", s => s.Dexterity)]],
            ["Sorcerer"]  = [[("Charisma", s => s.Charisma)]],
            ["Warlock"]   = [[("Charisma", s => s.Charisma)]],
            ["Wizard"]    = [[("Intelligence", s => s.Intelligence)]],
            ["Artificer"] = [[("Intelligence", s => s.Intelligence)]],
        };

    // Reduced proficiency grants when the class is taken as a multiclass (NOT the full first-class set).
    private static readonly Dictionary<string, string[]> ProficiencySubsets =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Barbarian"] = ["shields", "simple weapons", "martial weapons"],
            ["Bard"]      = ["light armor", "one skill", "one musical instrument"],
            ["Cleric"]    = ["light armor", "medium armor", "shields"],
            ["Druid"]     = ["light armor", "medium armor", "shields"],
            ["Fighter"]   = ["light armor", "medium armor", "shields", "simple weapons", "martial weapons"],
            ["Monk"]      = ["simple weapons", "shortswords"],
            ["Paladin"]   = ["light armor", "medium armor", "shields", "simple weapons", "martial weapons"],
            ["Ranger"]    = ["light armor", "medium armor", "shields", "simple weapons", "martial weapons", "one skill"],
            ["Rogue"]     = ["light armor", "one skill", "thieves' tools"],
            ["Sorcerer"]  = [],
            ["Warlock"]   = ["light armor", "simple weapons"],
            ["Wizard"]    = [],
            ["Artificer"] = ["light armor", "medium armor", "shields", "thieves' tools", "tinker's tools"],
        };

    public static PrereqResult CanMulticlassInto(string @class, CharacterSheet sheet)
    {
        if (!Prereqs.TryGetValue(@class, out var groups))
            return new PrereqResult(false, $"Unknown class '{@class}'.");

        // Allowed if ANY group is fully satisfied (each group is an all-of set of ability >= 13).
        foreach (var group in groups)
            if (group.All(req => req.Score(sheet) >= 13))
                return new PrereqResult(true, "");

        // Build a reason from the option groups (e.g. "Strength 13 or Dexterity 13").
        var options = groups.Select(g => string.Join(" and ", g.Select(r => $"{r.Ability} 13")));
        return new PrereqResult(false, $"Requires {string.Join(" or ", options)}.");
    }

    public static IReadOnlyList<string> MulticlassProficiencies(string @class) =>
        ProficiencySubsets.TryGetValue(@class, out var p) ? p : [];
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~MulticlassRulesTests`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add Features/Resolution/MulticlassRules.cs DndMcpAICsharpFun.Tests/Resolution/MulticlassRulesTests.cs
git commit -m "feat(resolution): deterministic multiclass validity (prereqs + proficiency subsets)"
```

---

## Task 5: Spellcasting composition — caster type, combined level, Warlock pact

**Files:**
- Create: `Features/Resolution/MulticlassSpellcasting.cs`
- Test: `DndMcpAICsharpFun.Tests/Resolution/MulticlassSpellcastingTests.cs` (create)

**Interfaces:**
- Produces:
  - `enum CasterType { None, Full, Half, Third, Pact }`
  - `MulticlassSpellcasting.Classify(ClassLevel c) : CasterType`
  - `MulticlassSpellcasting.CombinedCasterLevel(IEnumerable<ClassLevel> classes) : int`
  - `MulticlassSpellcasting.PactMagic(int SlotCount, int SlotLevel)` (record)
  - `MulticlassSpellcasting.WarlockPact(IEnumerable<ClassLevel> classes) : PactMagic?`
  - `MulticlassSpellcasting.SpellcastingAbility(string @class) : string?` (e.g. "Cleric"→"Wisdom"; null if non-caster)

- [ ] **Step 1: Write the failing test**

```csharp
// DndMcpAICsharpFun.Tests/Resolution/MulticlassSpellcastingTests.cs
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Resolution;

namespace DndMcpAICsharpFun.Tests.Resolution;

public sealed class MulticlassSpellcastingTests
{
    private static ClassLevel C(string cls, int lvl, string sub = "") =>
        new() { Class = cls, Level = lvl, Subclass = sub };

    [Fact]
    public void Full_casters_sum_directly()
    {
        MulticlassSpellcasting.CombinedCasterLevel([C("Wizard", 5), C("Cleric", 3)])
            .Should().Be(8);
    }

    [Fact]
    public void Half_casters_round_down_and_add_to_full()
    {
        // Paladin 6 -> floor(6/2)=3 ; + Sorcerer 2 (full) = 5
        MulticlassSpellcasting.CombinedCasterLevel([C("Paladin", 6), C("Sorcerer", 2)])
            .Should().Be(5);
    }

    [Fact]
    public void Artificer_rounds_up()
    {
        // Artificer 3 -> ceil(3/2)=2
        MulticlassSpellcasting.CombinedCasterLevel([C("Artificer", 3)]).Should().Be(2);
    }

    [Fact]
    public void Third_caster_subclass_rounds_down()
    {
        // Fighter 7 Eldritch Knight -> floor(7/3)=2 ; plain Fighter contributes 0
        MulticlassSpellcasting.CombinedCasterLevel([C("Fighter", 7, "Eldritch Knight")]).Should().Be(2);
        MulticlassSpellcasting.CombinedCasterLevel([C("Fighter", 7, "Champion")]).Should().Be(0);
    }

    [Fact]
    public void Warlock_is_excluded_from_combined_level_and_reported_separately()
    {
        var classes = new[] { C("Warlock", 3), C("Sorcerer", 2) };
        MulticlassSpellcasting.CombinedCasterLevel(classes).Should().Be(2); // only the Sorcerer
        var pact = MulticlassSpellcasting.WarlockPact(classes)!;
        pact.SlotCount.Should().Be(2);   // Warlock 3 => 2 pact slots
        pact.SlotLevel.Should().Be(2);   // Warlock 3 => 2nd-level slots
    }

    [Fact]
    public void Non_casters_yield_zero_and_no_pact()
    {
        MulticlassSpellcasting.CombinedCasterLevel([C("Rogue", 3), C("Fighter", 2)]).Should().Be(0);
        MulticlassSpellcasting.WarlockPact([C("Rogue", 3)]).Should().BeNull();
    }

    [Fact]
    public void Spellcasting_ability_is_per_class()
    {
        MulticlassSpellcasting.SpellcastingAbility("Cleric").Should().Be("Wisdom");
        MulticlassSpellcasting.SpellcastingAbility("Wizard").Should().Be("Intelligence");
        MulticlassSpellcasting.SpellcastingAbility("Rogue").Should().BeNull();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~MulticlassSpellcastingTests`
Expected: FAIL — `MulticlassSpellcasting` does not exist.

- [ ] **Step 3: Implement `MulticlassSpellcasting.cs`**

```csharp
using DndMcpAICsharpFun.Domain;

namespace DndMcpAICsharpFun.Features.Resolution;

public enum CasterType { None, Full, Half, Third, Pact }

/// <summary>
/// Deterministic multiclass spellcasting composition (PHB "Multiclassing" — Spellcasting).
/// Combined caster level = Σ full-caster levels + ⌊half-caster levels / 2⌋ (Artificer ⌈level/2⌉)
/// + ⌊third-caster levels / 3⌋. Warlock Pact Magic is EXCLUDED and reported separately.
/// </summary>
public static class MulticlassSpellcasting
{
    private static readonly HashSet<string> FullCasters =
        new(StringComparer.OrdinalIgnoreCase) { "Bard", "Cleric", "Druid", "Sorcerer", "Wizard" };

    private static readonly HashSet<string> HalfCasters =
        new(StringComparer.OrdinalIgnoreCase) { "Paladin", "Ranger" };

    // Third-caster ONLY at these Fighter/Rogue subclasses.
    private static readonly HashSet<string> ThirdCasterSubclasses =
        new(StringComparer.OrdinalIgnoreCase) { "Eldritch Knight", "Arcane Trickster" };

    private static readonly Dictionary<string, string> Abilities =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Bard"] = "Charisma", ["Cleric"] = "Wisdom", ["Druid"] = "Wisdom",
            ["Sorcerer"] = "Charisma", ["Wizard"] = "Intelligence",
            ["Paladin"] = "Charisma", ["Ranger"] = "Wisdom",
            ["Warlock"] = "Charisma", ["Artificer"] = "Intelligence",
            ["Eldritch Knight"] = "Intelligence", ["Arcane Trickster"] = "Intelligence",
        };

    public static CasterType Classify(ClassLevel c)
    {
        if (string.Equals(c.Class, "Warlock", StringComparison.OrdinalIgnoreCase)) return CasterType.Pact;
        if (FullCasters.Contains(c.Class)) return CasterType.Full;
        if (HalfCasters.Contains(c.Class)) return CasterType.Half;
        if (string.Equals(c.Class, "Artificer", StringComparison.OrdinalIgnoreCase)) return CasterType.Half;
        // Third-caster only for Fighter (Eldritch Knight) / Rogue (Arcane Trickster) — guard on the
        // parent class so a mismatched subclass string can't misclassify a non-caster as a caster.
        if (ThirdCasterSubclasses.Contains(c.Subclass)
            && (string.Equals(c.Class, "Fighter", StringComparison.OrdinalIgnoreCase)
                || string.Equals(c.Class, "Rogue", StringComparison.OrdinalIgnoreCase)))
            return CasterType.Third;
        return CasterType.None;
    }

    public static int CombinedCasterLevel(IEnumerable<ClassLevel> classes)
    {
        var full = 0; var half = 0; var artificer = 0; var third = 0;
        foreach (var c in classes)
        {
            switch (Classify(c))
            {
                case CasterType.Full: full += c.Level; break;
                case CasterType.Half:
                    if (string.Equals(c.Class, "Artificer", StringComparison.OrdinalIgnoreCase))
                        artificer += c.Level;   // rounded UP, per class
                    else
                        half += c.Level;        // Paladin/Ranger, rounded DOWN
                    break;
                case CasterType.Third: third += c.Level; break;
            }
        }
        return full + half / 2 + (artificer + 1) / 2 + third / 3;
    }

    public sealed record PactMagic(int SlotCount, int SlotLevel);

    public static PactMagic? WarlockPact(IEnumerable<ClassLevel> classes)
    {
        var warlock = classes.FirstOrDefault(c =>
            string.Equals(c.Class, "Warlock", StringComparison.OrdinalIgnoreCase));
        if (warlock is null || warlock.Level < 1) return null;
        var lvl = warlock.Level;
        // PHB Pact Magic: slot LEVEL advances to 2nd only at Warlock 3 (L1-2 are 1st-level slots).
        var slotLevel = lvl switch { 1 or 2 => 1, 3 or 4 => 2, 5 or 6 => 3, 7 or 8 => 4, _ => 5 };
        var slotCount = lvl switch { 1 => 1, <= 10 => 2, <= 16 => 3, _ => 4 };
        return new PactMagic(slotCount, slotLevel);
    }

    public static string? SpellcastingAbility(string @class) =>
        Abilities.GetValueOrDefault(@class);
}
```

Note the Warlock slot-count table: L1→1, L2-10→2, L11-16→3, L17-20→4 (PHB Warlock progression). The test only asserts L3, but keep the full table.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~MulticlassSpellcastingTests`
Expected: PASS (7 tests).

- [ ] **Step 5: Commit**

```bash
git add Features/Resolution/MulticlassSpellcasting.cs DndMcpAICsharpFun.Tests/Resolution/MulticlassSpellcastingTests.cs
git commit -m "feat(resolution): combined caster level + Warlock pact composition"
```

---

## Task 6: Seed the Multiclass Spellcaster slot table into StructuredTables

**Files:**
- Create: `Features/Resolution/MulticlassSlotTableSeeder.cs`
- Modify: `Extensions/WebApplicationExtensions.cs` (invoke after migrate)
- Modify: `Extensions/ServiceCollectionExtensions.cs` (register the seeder)
- Test: `DndMcpAICsharpFun.Tests/Persistence/MulticlassSlotTableSeederTests.cs` (create — real Postgres)

**Interfaces:**
- Produces:
  - `const string MulticlassSlotTableSeeder.TableId = "phb14.table.multiclass-spellcaster"`
  - `MulticlassSlotTableSeeder(IDbContextFactory<AppDbContext> dbf)` with `SeedAsync(CancellationToken) : Task` (idempotent upsert; columns `["casterLevel","1".."9"]`, 20 rows). Cells are `CanonicalCell(value, ProvenanceRef("phb14.block.multiclassing","PHB",164))`.

- [ ] **Step 1: Write the failing test — seeding is idempotent and cites PHB**

```csharp
// DndMcpAICsharpFun.Tests/Persistence/MulticlassSlotTableSeederTests.cs
// NOTE: mirror the [Collection]/fixture attributes of sibling persistence tests.
using System.Text.Json;
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Resolution;
using Microsoft.EntityFrameworkCore;

namespace DndMcpAICsharpFun.Tests.Persistence;

public sealed class MulticlassSlotTableSeederTests /* : <existing persistence base> */
{
    // ctor: capture IDbContextFactory<AppDbContext> from the shared fixture.
    // private readonly IDbContextFactory<AppDbContext> _dbf;

    [Fact]
    public async Task Seeds_20_rows_and_cites_phb_and_is_idempotent()
    {
        var seeder = new MulticlassSlotTableSeeder(_dbf);
        await seeder.SeedAsync(default);
        await seeder.SeedAsync(default); // second call must not duplicate

        await using var db = await _dbf.CreateDbContextAsync();
        var table = await db.StructuredTables
            .SingleAsync(t => t.CanonicalId == MulticlassSlotTableSeeder.TableId);
        var rows = await db.StructuredTableRows
            .Where(r => r.TableId == table.Id).OrderBy(r => r.RowIndex).ToListAsync();

        rows.Should().HaveCount(20);

        // Combined caster level 5 -> row index 4: 4/3/2 first/second/third-level slots.
        var cells = JsonSerializer.Deserialize<List<CanonicalCell>>(rows[4].CellsJson)!;
        cells[0].Value.Should().Be("5");   // casterLevel column
        cells[1].Value.Should().Be("4");   // 1st-level slots
        cells[2].Value.Should().Be("3");   // 2nd-level slots
        cells[3].Value.Should().Be("2");   // 3rd-level slots
        cells[1].Provenance!.SourceBook.Should().Be("PHB");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~MulticlassSlotTableSeederTests`
Expected: FAIL — `MulticlassSlotTableSeeder` does not exist.

- [ ] **Step 3: Implement `MulticlassSlotTableSeeder.cs`**

The 20 rows are the PHB Multiclass Spellcaster table (combined caster level 1-20 → slots per spell level 1-9). Columns: `casterLevel`, then 1..9.

```csharp
using System.Text.Json;
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DndMcpAICsharpFun.Features.Resolution;

/// <summary>
/// Idempotently seeds the PHB Multiclass Spellcaster combined-level → spell-slots table into
/// <see cref="StructuredTable"/> so multiclass slot resolution can cite a real provenance ref,
/// exactly as slice-1's breath-weapon path cites its projected table.
/// </summary>
public sealed class MulticlassSlotTableSeeder(IDbContextFactory<AppDbContext> dbf)
{
    public const string TableId = "phb14.table.multiclass-spellcaster";
    private static readonly ProvenanceRef Prov = new("phb14.block.multiclassing", "PHB", 164);

    // PHB p.164. Row i (0-based) = combined caster level i+1. Columns: slots for spell levels 1..9.
    private static readonly int[][] Slots =
    [
        [2,0,0,0,0,0,0,0,0], [3,0,0,0,0,0,0,0,0], [4,2,0,0,0,0,0,0,0], [4,3,0,0,0,0,0,0,0],
        [4,3,2,0,0,0,0,0,0], [4,3,3,0,0,0,0,0,0], [4,3,3,1,0,0,0,0,0], [4,3,3,2,0,0,0,0,0],
        [4,3,3,3,1,0,0,0,0], [4,3,3,3,2,0,0,0,0], [4,3,3,3,2,1,0,0,0], [4,3,3,3,2,1,0,0,0],
        [4,3,3,3,2,1,1,0,0], [4,3,3,3,2,1,1,0,0], [4,3,3,3,2,1,1,1,0], [4,3,3,3,2,1,1,1,0],
        [4,3,3,3,2,1,1,1,1], [4,3,3,3,3,1,1,1,1], [4,3,3,3,3,2,1,1,1], [4,3,3,3,3,2,2,1,1],
    ];

    public async Task SeedAsync(CancellationToken ct)
    {
        await using var db = await dbf.CreateDbContextAsync(ct);

        var table = await db.StructuredTables.FirstOrDefaultAsync(t => t.CanonicalId == TableId, ct);
        var columns = new[] { "casterLevel", "1", "2", "3", "4", "5", "6", "7", "8", "9" };
        if (table is null)
        {
            table = new StructuredTable
            {
                CanonicalId = TableId,
                Name = "Multiclass Spellcaster",
                ColumnsJson = JsonSerializer.Serialize(columns),
                SourceBook = "PHB",
            };
            db.StructuredTables.Add(table);
            await db.SaveChangesAsync(ct); // assign table.Id
        }

        // Idempotent: clear then reinsert rows.
        await db.StructuredTableRows.Where(r => r.TableId == table.Id).ExecuteDeleteAsync(ct);
        for (var i = 0; i < Slots.Length; i++)
        {
            var cells = new List<CanonicalCell> { new((i + 1).ToString(), Prov) };
            cells.AddRange(Slots[i].Select(n => new CanonicalCell(n.ToString(), Prov)));
            db.StructuredTableRows.Add(new StructuredTableRow
            {
                TableId = table.Id,
                RowIndex = i,
                CellsJson = JsonSerializer.Serialize(cells),
            });
        }
        await db.SaveChangesAsync(ct);
    }
}
```

- [ ] **Step 4: Register + invoke the seeder**

In `Extensions/ServiceCollectionExtensions.cs`, next to the existing resolution registrations (~line 110-111), add:

```csharp
        services.AddSingleton<DndMcpAICsharpFun.Features.Resolution.MulticlassSlotTableSeeder>();
```

In `Extensions/WebApplicationExtensions.cs`, in `MigrateDatabaseAsync` (after migrations are applied and the test user is seeded), resolve and run the seeder from the app's service scope:

```csharp
        var seeder = scope.ServiceProvider
            .GetRequiredService<DndMcpAICsharpFun.Features.Resolution.MulticlassSlotTableSeeder>();
        await seeder.SeedAsync(CancellationToken.None);
```

Use whatever scope/`serviceProvider` variable `MigrateDatabaseAsync` already has (open the method first with Serena to match the exact local names).

- [ ] **Step 5: Run tests + build**

Run: `dotnet test --filter FullyQualifiedName~MulticlassSlotTableSeederTests` then `dotnet build`
Expected: PASS; build 0/0.

- [ ] **Step 6: Commit**

```bash
git add Features/Resolution/MulticlassSlotTableSeeder.cs Extensions/ServiceCollectionExtensions.cs Extensions/WebApplicationExtensions.cs DndMcpAICsharpFun.Tests/Persistence/MulticlassSlotTableSeederTests.cs
git commit -m "feat(resolution): seed the Multiclass Spellcaster slot table with PHB provenance"
```

---

## Task 7: Resolve spell slots (single-vs-multi fork, cited)

**Files:**
- Modify: `Features/Resolution/CharacterResolutionService.cs`
- Test: `DndMcpAICsharpFun.Tests/Resolution/ResolveSpellSlotsTests.cs` (create — real Postgres; needs the seeded table)

**Interfaces:**
- Consumes: `MulticlassSpellcasting` (Task 5), `MulticlassSlotTableSeeder.TableId` (Task 6), `StructuredTable`/`StructuredTableRow`/`CanonicalCell` (existing).
- Produces: `ResolveForSheetAsync` handles feature `"spell slots"` → `ResolveSpellSlotsAsync(CharacterSheet, ct) : Task<ResolvedFact>`.
  - Value = a rendered slot line; Components = one `ResolvedComponent` per non-zero spell level (label `"level {n} slots"`, provenance = that cell's `Provenance`) + a separate `"pact magic"` component when a Warlock is present (no provenance — computed); Confidence `"ok"`, or `"needsReview"` when no caster classes / table row missing.

- [ ] **Step 1: Write the failing test**

```csharp
// DndMcpAICsharpFun.Tests/Resolution/ResolveSpellSlotsTests.cs
// NOTE: mirror the persistence fixture; seed the table first.
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Resolution;

namespace DndMcpAICsharpFun.Tests.Resolution;

public sealed class ResolveSpellSlotsTests /* : <existing persistence base> */
{
    // ctor: capture IDbContextFactory<AppDbContext> _dbf and HeroRepository (or a stub) from the fixture,
    // then: await new MulticlassSlotTableSeeder(_dbf).SeedAsync(default);
    // Build the service: new CharacterResolutionService(_dbf, heroes);

    [Fact]
    public async Task Multiclass_caster_reads_combined_level_row_and_cites_the_table()
    {
        var sheet = new CharacterSheet
        {
            Classes = [ new() { Class = "Paladin", Level = 6 }, new() { Class = "Sorcerer", Level = 2 } ],
            Charisma = 16,
        };
        // Resolve via the private-sheet path exposed by ResolveAsync over a saved snapshot, OR add a
        // small internal ResolveForSheetTestAsync — reuse whatever the breath-weapon tests use.
        var fact = await ResolveSpellSlotsForSheet(sheet); // helper wrapping the service

        fact.Feature.Should().Be("spell slots");
        // combined caster level 5 -> 4/3/2 first/second/third
        fact.Components.Should().Contain(c => c.Label == "level 1 slots" && c.Value == "4");
        fact.Components.Should().Contain(c => c.Label == "level 3 slots" && c.Value == "2");
        fact.Components.First(c => c.Label == "level 1 slots").Provenance!.SourceBook.Should().Be("PHB");
        fact.Confidence.Should().Be("ok");
    }

    [Fact]
    public async Task Warlock_multiclass_reports_pact_separately()
    {
        var sheet = new CharacterSheet
        {
            Classes = [ new() { Class = "Warlock", Level = 3 }, new() { Class = "Sorcerer", Level = 2 } ],
            Charisma = 16,
        };
        var fact = await ResolveSpellSlotsForSheet(sheet);

        // combined level counts only the Sorcerer (2) -> 3/0 slots
        fact.Components.Should().Contain(c => c.Label == "level 1 slots" && c.Value == "3");
        // pact is separate and carries no table provenance
        var pact = fact.Components.Single(c => c.Label == "pact magic");
        pact.Value.Should().Contain("2");  // 2 slots at 2nd level
        pact.Provenance.Should().BeNull();
    }

    [Fact]
    public async Task Non_caster_multiclass_has_no_spell_slots()
    {
        var sheet = new CharacterSheet
        {
            Classes = [ new() { Class = "Rogue", Level = 3 }, new() { Class = "Fighter", Level = 2 } ],
        };
        var fact = await ResolveSpellSlotsForSheet(sheet);
        fact.Value.Should().Contain("no spellcasting");
        fact.Confidence.Should().Be("needsReview");
    }
}
```

Implement the `ResolveSpellSlotsForSheet` helper the same way the breath-weapon tests reach the service (save a `HeroSnapshot` then `ResolveAsync(id, "spell slots")`, or an `internal` sheet-level entry point if the breath tests use one — match the existing pattern; do not invent a public API the breath tests don't already have).

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~ResolveSpellSlotsTests`
Expected: FAIL — feature `"spell slots"` throws `NotSupportedException`.

- [ ] **Step 3: Implement the fork in `CharacterResolutionService.cs`**

Extend `ResolveForSheetAsync` (currently only handles `"breath weapon"`):

```csharp
    private Task<ResolvedFact> ResolveForSheetAsync(CharacterSheet sheet, string feature, CancellationToken ct)
    {
        if (feature.Equals("breath weapon", StringComparison.OrdinalIgnoreCase))
            return ResolveBreathWeaponAsync(sheet, ct);
        if (feature.Equals("spell slots", StringComparison.OrdinalIgnoreCase))
            return ResolveSpellSlotsAsync(sheet, ct);

        throw new NotSupportedException($"feature not supported: {feature}");
    }
```

Add the method (uses the seeded table + `MulticlassSpellcasting`):

```csharp
    private async Task<ResolvedFact> ResolveSpellSlotsAsync(CharacterSheet sheet, CancellationToken ct)
    {
        var combined = MulticlassSpellcasting.CombinedCasterLevel(sheet.Classes);
        var pact = MulticlassSpellcasting.WarlockPact(sheet.Classes);

        if (combined == 0 && pact is null)
            return new ResolvedFact("spell slots", "no spellcasting", [], "needsReview");

        var components = new List<ResolvedComponent>();
        var rendered = new List<string>();

        if (combined > 0)
        {
            await using var db = await dbf.CreateDbContextAsync(ct);
            var table = await db.StructuredTables
                .FirstOrDefaultAsync(t => t.CanonicalId == MulticlassSlotTableSeeder.TableId, ct);
            var row = table is null ? null : await db.StructuredTableRows
                .FirstOrDefaultAsync(r => r.TableId == table.Id && r.RowIndex == combined - 1, ct);

            if (table is null || row is null)
                return new ResolvedFact("spell slots", "combined caster level " + combined, [], "needsReview");

            var columns = JsonSerializer.Deserialize<List<string>>(table.ColumnsJson, JsonOpts) ?? [];
            var cells = JsonSerializer.Deserialize<List<CanonicalCell>>(row.CellsJson, JsonOpts) ?? [];

            // columns[0] is "casterLevel"; columns 1..9 are spell levels.
            for (var lvl = 1; lvl <= 9 && lvl < columns.Count && lvl < cells.Count; lvl++)
            {
                var count = cells[lvl].Value;
                if (count == "0" || string.IsNullOrWhiteSpace(count)) continue;
                components.Add(new ResolvedComponent($"level {lvl} slots", count, cells[lvl].Provenance));
                rendered.Add($"L{lvl}:{count}");
            }
        }

        if (pact is not null)
        {
            // Pact Magic is computed (PHB Warlock progression), not a table cell -> no provenance (COR-20).
            components.Add(new ResolvedComponent(
                "pact magic", $"{pact.SlotCount} slots at level {pact.SlotLevel}", null));
            rendered.Add($"pact {pact.SlotCount}@L{pact.SlotLevel}");
        }

        var value = rendered.Count > 0 ? string.Join(", ", rendered) : "no spellcasting";
        return new ResolvedFact("spell slots", value, components, "ok");
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~ResolveSpellSlotsTests`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add Features/Resolution/CharacterResolutionService.cs DndMcpAICsharpFun.Tests/Resolution/ResolveSpellSlotsTests.cs
git commit -m "feat(resolution): multiclass-aware spell-slot resolution with cited table"
```

---

## Task 8: Resolve spell save DC per caster class

**Files:**
- Modify: `Features/Resolution/CharacterResolutionService.cs`
- Test: `DndMcpAICsharpFun.Tests/Resolution/ResolveSpellSaveDcTests.cs` (create)

**Interfaces:**
- Consumes: `MulticlassSpellcasting.SpellcastingAbility` (Task 5), `CharacterSheet.Modifier`, `ProficiencyBonus`.
- Produces: `ResolveForSheetAsync` handles feature `"spell save dc"` → `ResolveSpellSaveDcAsync(CharacterSheet, ct) : Task<ResolvedFact>`. One `ResolvedComponent` per caster class (label `"{Class} save DC"`, value = `8 + ProficiencyBonus + abilityMod`), no provenance (computed). Confidence `"needsReview"` if no caster classes.

- [ ] **Step 1: Write the failing test**

```csharp
// DndMcpAICsharpFun.Tests/Resolution/ResolveSpellSaveDcTests.cs
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Resolution;

namespace DndMcpAICsharpFun.Tests.Resolution;

public sealed class ResolveSpellSaveDcTests /* : <existing persistence base or plain, matching breath tests> */
{
    [Fact]
    public async Task Each_caster_class_reports_its_own_dc()
    {
        var sheet = new CharacterSheet
        {
            Classes = [ new() { Class = "Cleric", Level = 3 }, new() { Class = "Wizard", Level = 2 } ],
            Wisdom = 16,       // Cleric: mod +3
            Intelligence = 14, // Wizard: mod +2
        };
        // total level 5 -> PB +3
        var fact = await ResolveSpellSaveDcForSheet(sheet);

        fact.Components.Should().Contain(c => c.Label == "Cleric save DC" && c.Value == "14"); // 8+3+3
        fact.Components.Should().Contain(c => c.Label == "Wizard save DC" && c.Value == "13"); // 8+3+2
    }

    [Fact]
    public async Task Non_caster_reports_needs_review()
    {
        var sheet = new CharacterSheet { Classes = [ new() { Class = "Barbarian", Level = 5 } ] };
        var fact = await ResolveSpellSaveDcForSheet(sheet);
        fact.Confidence.Should().Be("needsReview");
    }
}
```

Wire `ResolveSpellSaveDcForSheet` the same way as the slots/breath test helper.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~ResolveSpellSaveDcTests`
Expected: FAIL — feature `"spell save dc"` not supported.

- [ ] **Step 3: Implement**

Add to `ResolveForSheetAsync`:

```csharp
        if (feature.Equals("spell save dc", StringComparison.OrdinalIgnoreCase))
            return ResolveSpellSaveDcAsync(sheet, ct);
```

Add the method (synchronous body wrapped in `Task.FromResult`):

```csharp
    private Task<ResolvedFact> ResolveSpellSaveDcAsync(CharacterSheet sheet, CancellationToken ct)
    {
        var pb = sheet.ProficiencyBonus;
        var components = new List<ResolvedComponent>();
        foreach (var c in sheet.Classes)
        {
            var ability = MulticlassSpellcasting.SpellcastingAbility(c.Class);
            if (ability is null) continue;
            var score = ability switch
            {
                "Strength" => sheet.Strength,
                "Dexterity" => sheet.Dexterity,
                "Constitution" => sheet.Constitution,
                "Intelligence" => sheet.Intelligence,
                "Wisdom" => sheet.Wisdom,
                "Charisma" => sheet.Charisma,
                _ => 10,
            };
            var dc = 8 + pb + CharacterSheet.Modifier(score);
            // Computed value (8 + PB + ability mod) -> no provenance (COR-20).
            components.Add(new ResolvedComponent($"{c.Class} save DC", dc.ToString(), null));
        }

        if (components.Count == 0)
            return Task.FromResult(new ResolvedFact("spell save dc", "no spellcasting", [], "needsReview"));

        var value = string.Join(", ", components.Select(c => $"{c.Label} {c.Value}"));
        return Task.FromResult(new ResolvedFact("spell save dc", value, components, "ok"));
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~ResolveSpellSaveDcTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add Features/Resolution/CharacterResolutionService.cs DndMcpAICsharpFun.Tests/Resolution/ResolveSpellSaveDcTests.cs
git commit -m "feat(resolution): per-caster-class spell save DC"
```

---

## Task 9: Resolve multiclass validity (target-class check)

**Files:**
- Modify: `Features/Resolution/CharacterResolutionService.cs`
- Test: `DndMcpAICsharpFun.Tests/Resolution/ResolveMulticlassValidityTests.cs` (create)

**Interfaces:**
- Consumes: `MulticlassRules` (Task 4).
- Produces: `CharacterResolutionService.ResolveMulticlassValidityForUserAsync(long heroSnapshotId, long userId, string targetClass, CancellationToken) : Task<ResolvedFact>` and an internal sheet-level `ResolveMulticlassValidityAsync(CharacterSheet sheet, string targetClass) : ResolvedFact`.
  - Value = "allowed"/"not allowed: {reason}"; Components: one `"prerequisite"` component (value = reason or "met") + one `"proficiencies"` component (value = comma-joined subset); no provenance (deterministic rule). Confidence `"ok"`.

This is a distinct entry point (not routed through the feature string) because it takes a `targetClass` argument.

- [ ] **Step 1: Write the failing test**

```csharp
// DndMcpAICsharpFun.Tests/Resolution/ResolveMulticlassValidityTests.cs
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Resolution;

namespace DndMcpAICsharpFun.Tests.Resolution;

public sealed class ResolveMulticlassValidityTests
{
    [Fact]
    public void Blocked_multiclass_reports_the_failed_prerequisite()
    {
        var sheet = new CharacterSheet { Classes = [ new() { Class = "Wizard", Level = 5 } ], Dexterity = 12 };
        var fact = CharacterResolutionService.ResolveMulticlassValidity(sheet, "Rogue");

        fact.Value.Should().StartWith("not allowed");
        fact.Components.Should().Contain(c => c.Label == "prerequisite" && c.Value.Contains("Dexterity 13"));
    }

    [Fact]
    public void Allowed_multiclass_lists_the_reduced_proficiency_subset()
    {
        var sheet = new CharacterSheet { Classes = [ new() { Class = "Wizard", Level = 5 } ], Dexterity = 14 };
        var fact = CharacterResolutionService.ResolveMulticlassValidity(sheet, "Fighter");

        fact.Value.Should().Be("allowed");
        var profs = fact.Components.Single(c => c.Label == "proficiencies").Value;
        profs.Should().Contain("martial weapons");
        profs.Should().NotContain("heavy armor");
    }
}
```

Note: the sheet-level method is a **static** pure function (`ResolveMulticlassValidity`) so it is trivially unit-testable without a DB; the user-scoped async wrapper just loads the snapshot then calls it.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~ResolveMulticlassValidityTests`
Expected: FAIL — method does not exist.

- [ ] **Step 3: Implement**

Add to `CharacterResolutionService`:

```csharp
    /// <summary>
    /// Deterministic multiclass-validity answer for a target class: prerequisite check + reduced
    /// proficiency subset. Pure (no DB) so it serves non-caster combos with zero spellcasting.
    /// </summary>
    public static ResolvedFact ResolveMulticlassValidity(CharacterSheet sheet, string targetClass)
    {
        var prereq = MulticlassRules.CanMulticlassInto(targetClass, sheet);
        var profs = MulticlassRules.MulticlassProficiencies(targetClass);
        var components = new List<ResolvedComponent>
        {
            new("prerequisite", prereq.Allowed ? "met" : prereq.Reason, null),
            new("proficiencies", string.Join(", ", profs), null),
        };
        var value = prereq.Allowed ? "allowed" : $"not allowed: {prereq.Reason}";
        return new ResolvedFact($"multiclass into {targetClass}", value, components, "ok");
    }

    /// <summary>User-scoped wrapper: enforces snapshot ownership (SEC-08) then runs the pure check.</summary>
    public async Task<ResolvedFact> ResolveMulticlassValidityForUserAsync(
        long heroSnapshotId, long userId, string targetClass, CancellationToken ct = default)
    {
        var snapshot = await heroes.GetSnapshotForUserAsync(heroSnapshotId, userId);
        if (snapshot is null)
            throw new UnauthorizedAccessException("Hero snapshot not found or not owned by the caller.");
        return ResolveMulticlassValidity(snapshot.Sheet, targetClass);
    }
```

- [ ] **Step 4: Run tests + build**

Run: `dotnet test --filter FullyQualifiedName~ResolveMulticlassValidityTests` then `dotnet build`
Expected: PASS; 0/0.

- [ ] **Step 5: Commit**

```bash
git add Features/Resolution/CharacterResolutionService.cs DndMcpAICsharpFun.Tests/Resolution/ResolveMulticlassValidityTests.cs
git commit -m "feat(resolution): multiclass validity resolution (prereq + proficiency subset)"
```

---

## Task 10: MCP surface — expose slots / save DC / check_multiclass

**Files:**
- Modify: `Features/Chat/DndChatService.cs`

**Interfaces:**
- Consumes: `resolutionService.ResolveForUserAsync(...)` (existing, now handles `"spell slots"` / `"spell save dc"`), `resolutionService.ResolveMulticlassValidityForUserAsync(...)` (Task 9).

Spell slots and spell save DC ride the EXISTING `resolve_character_feature` tool (they are just new feature strings) — only its description needs to advertise them. Multiclass validity needs its own tool because it takes a `targetClass` argument.

- [ ] **Step 1: Broaden the `resolve_character_feature` description**

In `DndChatService.SendAsync`, update the existing `resolve_character_feature` registration's `description` to list the new features:

```csharp
                description: "Compute a character-specific, cited rule fact for a hero snapshot the " +
                    "signed-in user owns. Supported features: \"breath weapon\", \"spell slots\" " +
                    "(multiclass-aware combined caster level; Warlock pact reported separately), " +
                    "\"spell save dc\" (one value per caster class). Returns the value plus the rule " +
                    "components and their source provenance."));
```

- [ ] **Step 2: Add the `check_multiclass` per-user tool**

Immediately after the `resolve_character_feature` registration (inside the same `if (long.TryParse(idClaim, out var userId))` block), add:

```csharp
            toolList.Add(AIFunctionFactory.Create(
                (long heroSnapshotId, string targetClass, CancellationToken toolCt) =>
                    resolutionService.ResolveMulticlassValidityForUserAsync(
                        heroSnapshotId, userId, targetClass, toolCt),
                name: "check_multiclass",
                description: "Check whether the signed-in user's hero snapshot may multiclass into a " +
                    "given class (targetClass, e.g. \"Rogue\"). Returns allowed/not-allowed with the " +
                    "failed ability-score prerequisite and the reduced proficiency subset the class " +
                    "grants. Works for any combination, caster or not."));
```

- [ ] **Step 3: Build**

Run: `dotnet build`
Expected: 0/0. (These are in-process `AIFunction`s — NO `MapGet`/`MapPost`, so `.http`/`.insomnia` are unchanged. Confirm with `grep -rnE "MapGet|MapPost" Features/Chat` returning nothing new.)

- [ ] **Step 4: Commit**

```bash
git add Features/Chat/DndChatService.cs
git commit -m "feat(mcp): expose multiclass spell-slot/save-DC features + check_multiclass tool"
```

---

## Task 11: Full validation & live check

**Files:**
- Modify (if needed): docs touching character/resolution config

- [ ] **Step 1: Full build (warnings-as-errors)**

Run: `dotnet build`
Expected: 0 warnings / 0 errors.

- [ ] **Step 2: Full non-persistence suite**

Run: `dotnet test --filter "FullyQualifiedName!~Persistence"`
Expected: all green (includes all `MulticlassRules`/`MulticlassSpellcasting`/`CharacterSheet*`/`ResolveMulticlassValidity`/`ResolveSpellSaveDc` unit tests).

- [ ] **Step 3: Persistence suite (Docker running)**

Run: `dotnet test --filter "FullyQualifiedName~Persistence"`
Expected: green — includes `HeroSnapshotMulticlassRoundTripTests`, `MulticlassSlotTableSeederTests`, `ResolveSpellSlotsTests`. If Docker is down, start it (`docker compose up -d` or the test container prereq) first.

- [ ] **Step 4: Live sanity check (arithmetic + provenance + non-caster path)**

Confirm the four spec scenarios by unit assertions already covered, then eyeball via a scratch xunit fact or the chat tool:
- Paladin 6 / Sorcerer 2 → combined level 5 → slots `L1:4, L2:3, L3:2`, provenance PHB.
- Warlock 3 / Sorcerer 2 → combined counts only Sorcerer (2); pact `2 slots at level 2` separate.
- Single-class Wizard 5 → combined level 5, direct path.
- Rogue 3 / Fighter 2 → `check_multiclass` for "Fighter" allowed with martial-weapons subset; NO spellcasting computed (combined level 0 → "no spellcasting").

- [ ] **Step 5: Docs — only if a documented surface changed**

`grep -rn "resolve_character_feature\|multiclass" docs README.md CLAUDE.md`. If character/resolution features are enumerated anywhere, add `spell slots`, `spell save dc`, and `check_multiclass`. Then lint markdown: `pnpm lint:md:fix && pnpm lint:md` (must report `0 error(s)`). If nothing documents the feature surface, this is a no-op — note it. No HTTP route changed → `.http`/`.insomnia` untouched.

- [ ] **Step 6: Final commit**

```bash
git add -A
git commit -m "chore(multiclass): validation pass — build 0/0, suites green, docs synced"
```

---

## Task 12: Single-class vs multiclass spell-slot fork (correctness fix)

**Why:** Task 7 shipped `ResolveSpellSlotsAsync` reading the Multiclass Spellcaster combined-level table for EVERY sheet. That coincides with the correct answer for full casters and Artificer, but is WRONG for single-class half-casters (Paladin/Ranger) and third-casters (Eldritch Knight / Arcane Trickster), because the multiclass table rounds their caster level down (⌊N/2⌋, ⌊N/3⌋) whereas a single-class caster uses its own class progression at full class level. The spec's *"Resolution forks single-class vs multiclass"* requirement mandates the fork on the number of spellcasting classes: **≥2 spellcasting classes → combined table; exactly 1 → that class's own progression.** (Warlock Pact Magic is not counted as a spellcasting class for this fork and is always a separate component.)

**Files:**
- Modify: `Features/Resolution/MulticlassSpellcasting.cs` (add `SlotSource` + `ResolveSlotSource`)
- Modify: `Features/Resolution/MulticlassSlotTableSeeder.cs` (seed two more tables)
- Modify: `Features/Resolution/CharacterResolutionService.cs` (fork on `ResolveSlotSource`)
- Test: `DndMcpAICsharpFun.Tests/Resolution/MulticlassSpellcastingTests.cs`, `MulticlassSlotTableSeederTests.cs`, `ResolveSpellSlotsTests.cs`

**Interfaces:**
- Produces: `MulticlassSpellcasting.SlotSource(string Kind, int Level)` (record; `Kind` ∈ `"multiclass"|"half"|"third"|"none"`) and `ResolveSlotSource(IEnumerable<ClassLevel>) : SlotSource`.
- Produces: `MulticlassSlotTableSeeder.HalfCasterTableId = "phb14.table.half-caster-slots"`, `ThirdCasterTableId = "phb14.table.third-caster-slots"`.

- [ ] **Step 1: Add `ResolveSlotSource` + unit tests to `MulticlassSpellcasting`**

```csharp
    /// <summary>Which slot table a character reads and at what level: multiclass = combined-caster-level
    /// table; half/third = the single-class Paladin/Ranger or EK/AT progression at the class's own level;
    /// none = no non-pact spellcasting class. Warlock (Pact) is never counted here.</summary>
    public sealed record SlotSource(string Kind, int Level);

    public static SlotSource ResolveSlotSource(IEnumerable<ClassLevel> classes)
    {
        var casters = classes
            .Where(c => Classify(c) is CasterType.Full or CasterType.Half or CasterType.Third)
            .ToList();
        if (casters.Count == 0) return new SlotSource("none", 0);
        if (casters.Count == 1)
        {
            var c = casters[0];
            if (string.Equals(c.Class, "Paladin", StringComparison.OrdinalIgnoreCase)
                || string.Equals(c.Class, "Ranger", StringComparison.OrdinalIgnoreCase))
                return new SlotSource("half", c.Level);
            if (ThirdCasterSubclasses.Contains(c.Subclass))
                return new SlotSource("third", c.Level);
            // Single full caster or Artificer: the combined table at the class's combined level coincides
            // with the class's own progression, so reuse it.
            return new SlotSource("multiclass", CombinedCasterLevel(casters));
        }
        return new SlotSource("multiclass", CombinedCasterLevel(casters));
    }
```

Tests (add to `MulticlassSpellcastingTests`):

```csharp
    [Fact]
    public void Single_class_paladin_reads_the_half_caster_table_at_class_level()
    {
        var s = MulticlassSpellcasting.ResolveSlotSource([C("Paladin", 5)]);
        s.Kind.Should().Be("half"); s.Level.Should().Be(5);   // NOT combined level 2
    }

    [Fact]
    public void Single_class_eldritch_knight_reads_the_third_caster_table_at_class_level()
    {
        var s = MulticlassSpellcasting.ResolveSlotSource([C("Fighter", 7, "Eldritch Knight")]);
        s.Kind.Should().Be("third"); s.Level.Should().Be(7);  // NOT combined level 2
    }

    [Fact]
    public void Single_class_wizard_uses_the_multiclass_table_which_coincides()
    {
        var s = MulticlassSpellcasting.ResolveSlotSource([C("Wizard", 5)]);
        s.Kind.Should().Be("multiclass"); s.Level.Should().Be(5);
    }

    [Fact]
    public void Two_spellcasting_classes_use_the_combined_multiclass_table()
    {
        var s = MulticlassSpellcasting.ResolveSlotSource([C("Paladin", 6), C("Sorcerer", 2)]);
        s.Kind.Should().Be("multiclass"); s.Level.Should().Be(5);  // ⌊6/2⌋+2
    }

    [Fact]
    public void One_spellcasting_class_plus_warlock_reads_that_class_table_not_combined()
    {
        // Warlock is Pact (not a "spellcasting class" for the fork); Paladin is the sole caster class.
        var s = MulticlassSpellcasting.ResolveSlotSource([C("Warlock", 3), C("Paladin", 2)]);
        s.Kind.Should().Be("half"); s.Level.Should().Be(2);
    }
```

- [ ] **Step 2: Seed the half-caster and third-caster tables (extend `MulticlassSlotTableSeeder`)**

Add the two ids and their 20-row progressions, and seed all three tables in `SeedAsync` (factor the single-table upsert into a private helper `SeedTableAsync(db, id, name, rows, ct)` so the three calls share it). The half-caster table is the PHB Paladin/Ranger progression; the third-caster table is the PHB Eldritch Knight / Arcane Trickster progression. Row i (0-based) = class level i+1; nine columns = slots for spell levels 1..9.

```csharp
    public const string HalfCasterTableId = "phb14.table.half-caster-slots";
    public const string ThirdCasterTableId = "phb14.table.third-caster-slots";

    // PHB Paladin/Ranger. L1 has no slots; half-casters cap at 5th-level spells.
    private static readonly int[][] HalfCasterSlots =
    [
        [0,0,0,0,0,0,0,0,0], [2,0,0,0,0,0,0,0,0], [3,0,0,0,0,0,0,0,0], [3,0,0,0,0,0,0,0,0],
        [4,2,0,0,0,0,0,0,0], [4,2,0,0,0,0,0,0,0], [4,3,0,0,0,0,0,0,0], [4,3,0,0,0,0,0,0,0],
        [4,3,2,0,0,0,0,0,0], [4,3,2,0,0,0,0,0,0], [4,3,3,0,0,0,0,0,0], [4,3,3,0,0,0,0,0,0],
        [4,3,3,1,0,0,0,0,0], [4,3,3,1,0,0,0,0,0], [4,3,3,2,0,0,0,0,0], [4,3,3,2,0,0,0,0,0],
        [4,3,3,3,1,0,0,0,0], [4,3,3,3,1,0,0,0,0], [4,3,3,3,2,0,0,0,0], [4,3,3,3,2,0,0,0,0],
    ];

    // PHB Eldritch Knight / Arcane Trickster. No slots before class level 3; third-casters cap at 4th-level.
    private static readonly int[][] ThirdCasterSlots =
    [
        [0,0,0,0,0,0,0,0,0], [0,0,0,0,0,0,0,0,0], [2,0,0,0,0,0,0,0,0], [3,0,0,0,0,0,0,0,0],
        [3,0,0,0,0,0,0,0,0], [3,0,0,0,0,0,0,0,0], [4,2,0,0,0,0,0,0,0], [4,2,0,0,0,0,0,0,0],
        [4,2,0,0,0,0,0,0,0], [4,3,0,0,0,0,0,0,0], [4,3,0,0,0,0,0,0,0], [4,3,0,0,0,0,0,0,0],
        [4,3,2,0,0,0,0,0,0], [4,3,2,0,0,0,0,0,0], [4,3,2,0,0,0,0,0,0], [4,3,3,0,0,0,0,0,0],
        [4,3,3,0,0,0,0,0,0], [4,3,3,0,0,0,0,0,0], [4,3,3,1,0,0,0,0,0], [4,3,3,1,0,0,0,0,0],
    ];
```

`SeedAsync` now seeds `(TableId, "Multiclass Spellcaster", Slots)`, `(HalfCasterTableId, "Half-Caster Slots", HalfCasterSlots)`, `(ThirdCasterTableId, "Third-Caster Slots", ThirdCasterSlots)` via the shared helper. Add seeder-test assertions: half table row 4 (Paladin/Ranger level 5) = `4/2`; third table row 6 (EK/AT level 7) = `4/2`; each table has 20 rows; provenance PHB.

- [ ] **Step 3: Fork `ResolveSpellSlotsAsync` on `ResolveSlotSource`**

Replace the `combined`/table selection at the top of `ResolveSpellSlotsAsync` with the fork. Keep the rest (per-non-zero-level components with the cell's provenance; Warlock pact separate with null provenance; needsReview on missing table/row) identical.

```csharp
    private async Task<ResolvedFact> ResolveSpellSlotsAsync(CharacterSheet sheet, CancellationToken ct)
    {
        var source = MulticlassSpellcasting.ResolveSlotSource(sheet.Classes);
        var pact = MulticlassSpellcasting.WarlockPact(sheet.Classes);

        if (source.Kind == "none" && pact is null)
            return new ResolvedFact("spell slots", "no spellcasting", [], "needsReview");

        var components = new List<ResolvedComponent>();
        var rendered = new List<string>();

        if (source.Kind != "none" && source.Level >= 1)
        {
            var tableId = source.Kind switch
            {
                "half"  => MulticlassSlotTableSeeder.HalfCasterTableId,
                "third" => MulticlassSlotTableSeeder.ThirdCasterTableId,
                _        => MulticlassSlotTableSeeder.TableId,
            };

            await using var db = await dbf.CreateDbContextAsync(ct);
            var table = await db.StructuredTables.FirstOrDefaultAsync(t => t.CanonicalId == tableId, ct);
            var row = table is null ? null : await db.StructuredTableRows
                .FirstOrDefaultAsync(r => r.TableId == table.Id && r.RowIndex == source.Level - 1, ct);

            if (table is null || row is null)
            {
                // Table/row missing: still surface the (table-independent) pact component if present.
                if (pact is not null)
                    return new ResolvedFact("spell slots",
                        $"pact {pact.SlotCount}@L{pact.SlotLevel}",
                        [new ResolvedComponent("pact magic", $"{pact.SlotCount} slots at level {pact.SlotLevel}", null)],
                        "needsReview");
                return new ResolvedFact("spell slots", "caster level " + source.Level, [], "needsReview");
            }

            var columns = JsonSerializer.Deserialize<List<string>>(table.ColumnsJson, JsonOpts) ?? [];
            var cells = JsonSerializer.Deserialize<List<CanonicalCell>>(row.CellsJson, JsonOpts) ?? [];
            for (var lvl = 1; lvl <= 9 && lvl < columns.Count && lvl < cells.Count; lvl++)
            {
                var count = cells[lvl].Value;
                if (count == "0" || string.IsNullOrWhiteSpace(count)) continue;
                components.Add(new ResolvedComponent($"level {lvl} slots", count, cells[lvl].Provenance));
                rendered.Add($"L{lvl}:{count}");
            }
        }

        if (pact is not null)
        {
            components.Add(new ResolvedComponent(
                "pact magic", $"{pact.SlotCount} slots at level {pact.SlotLevel}", null));
            rendered.Add($"pact {pact.SlotCount}@L{pact.SlotLevel}");
        }

        var value = rendered.Count > 0 ? string.Join(", ", rendered) : "no spellcasting";
        var confidence = components.Count > 0 ? "ok" : "needsReview";
        return new ResolvedFact("spell slots", value, components, confidence);
    }
```

(This also resolves the Task-7 Minors: the missing-table branch now surfaces the pact, and `confidence` derives from `components.Count` rather than a hardcoded `"ok"`.)

- [ ] **Step 4: Add resolution regression tests (`ResolveSpellSlotsTests`)**

```csharp
    [Fact]
    public async Task Single_class_paladin_5_reads_the_half_caster_table_not_combined()
    {
        var sheet = new CharacterSheet { Classes = [ new() { Class = "Paladin", Level = 5 } ], Charisma = 16 };
        var fact = await ResolveSpellSlotsForSheet(sheet);
        fact.Components.Should().Contain(c => c.Label == "level 1 slots" && c.Value == "4");
        fact.Components.Should().Contain(c => c.Label == "level 2 slots" && c.Value == "2");
        fact.Confidence.Should().Be("ok");
    }

    [Fact]
    public async Task Single_class_wizard_5_is_unchanged()
    {
        var sheet = new CharacterSheet { Classes = [ new() { Class = "Wizard", Level = 5 } ], Intelligence = 16 };
        var fact = await ResolveSpellSlotsForSheet(sheet);
        fact.Components.Should().Contain(c => c.Label == "level 1 slots" && c.Value == "4");
        fact.Components.Should().Contain(c => c.Label == "level 3 slots" && c.Value == "2");
    }
```

- [ ] **Step 5: Verify + commit**

`dotnet test --filter FullyQualifiedName~MulticlassSpellcastingTests` + `~MulticlassSlotTableSeederTests` + `~ResolveSpellSlotsTests` (dangerouslyDisableSandbox), full non-persistence suite green, `dotnet build` 0/0. Commit: `fix(resolution): fork single-class half/third caster spell slots onto their own tables`.

---

## Self-Review

**Spec coverage** (each ADDED requirement → task):
- *Character sheet models per-class levels* → Task 1 (Classes source-of-truth, derived flat fields, PB from total). ✓
- *Existing single-class heroes migrate tolerantly* → Task 2 (JSON round-trip) + Task 3 (DB round-trip). ✓
- *Multiclass validity checked deterministically for any combination* → Task 4 (prereqs + proficiency subsets) + Task 9 (resolution, non-caster path). ✓
- *Spellcasting slots compose via combined caster level* (Warlock separate) → Task 5 (arithmetic) + Task 6 (seeded combined table) + Task 7 (multiclass resolution, provenance) + Task 12 (single-class half/third-caster tables + the fork). ✓
- *Spell save DC / attack per caster class* → Task 8 (per-class save DC AND per-class attack bonus, via the shared `PerCasterClass` helper). ✓
- *Resolution forks single-vs-multi and is exposed via MCP* → Task 12 (the ≥2-spellcasting-class fork onto the combined vs single-class tables) + Task 10 (MCP tools). ✓

**Placeholder scan:** every code step contains full code; the only intentional "match the existing fixture" notes are in the persistence tests, which must copy the sibling `[Collection]`/fixture wiring (an existing pattern, not an invented API).

**Type consistency:** `ClassLevel`, `Classes`, `SetSingleClass`, `MulticlassRules.CanMulticlassInto`/`MulticlassProficiencies`/`PrereqResult`, `MulticlassSpellcasting.CombinedCasterLevel`/`WarlockPact`/`PactMagic`/`SpellcastingAbility`/`Classify`/`CasterType`, `MulticlassSlotTableSeeder.TableId`/`SeedAsync`, `ResolveMulticlassValidity`/`ResolveMulticlassValidityForUserAsync`, feature strings `"spell slots"`/`"spell save dc"` — used identically across tasks.

**Open item for the implementer:** in Tasks 7-9 the sheet-level resolution entry point in tests must reuse whatever the existing breath-weapon tests use to reach `CharacterResolutionService` (save a snapshot + `ResolveAsync`, or an internal helper) — do not add a new public API the breath tests don't already rely on.
