# Structured Armor & AC — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Capture worn armor structurally on `CharacterSheet` and resolve an `armor class` fact deterministically (armor base + Dex-by-category + shield + magic + Barbarian/Monk Unarmored Defense), grounded/`needsReview`, never fabricated.

**Architecture:** A `WornArmor` value on `CharacterSheet` (JSON column — NO migration), a static `ArmorCatalog` (PHB name → base AC + category), a PURE `armor class` resolver on `CharacterResolutionService` (mirrors `ResolveSavingThrows` — no DB), and a capture-only hero-editor control. The stored manual `ArmorClass` int is untouched.

**Tech Stack:** C# / .NET 10, xunit + FluentAssertions (pure unit tests — no Postgres), Blazor Server, Playwright for the UI screenshot. Serena for all tracked files.

## Global Constraints
- **Serena only** for tracked files (subagents too; stop after one >2-min hang). `.superpowers/` report files are git-ignored → built-in Write for those only.
- **Work on `main`**; commit each reviewed task. Build 0/0 (warnings-as-errors). `dotnet` needs `dangerouslyDisableSandbox: true` (git-crypt). Ignore LSP false CS0246 on tests.
- **NO EF migration** (`CharacterSheet` is a JSON column — a new property deserializes to its default). If a migration file appears, something is wrong — do not commit it.
- No HTTP endpoint change; no `.http`/insomnia change. The resolver is PURE (no DB) — its tests are plain unit tests (no Docker).
- Grounding: computed values carry null provenance; unknown armor → `needsReview`; **never fabricate a base AC**.
- Task 4 is a presentational + data-capture UI change — behavior unchanged (full suite stays green), and it MUST be screenshotted on the running app (rebuild the container first).

**Known shapes (verified):**
```csharp
// Domain/CharacterSheet.cs: sealed class; int Strength..Charisma; static int Modifier(int); JSON-persisted (AppDbContext JSON column).
//   Existing sub-objects use property initializers (e.g. List<ClassLevel> Classes = []). sheet.Class => Classes[0].Class or "".
// Features/Resolution/CharacterResolutionService.cs: ResolveForSheetAsync dispatches feature strings; existing PURE static
//   `public static ResolvedFact ResolveSavingThrows(CharacterSheet sheet)` + dispatch `return Task.FromResult(ResolveSavingThrows(sheet));`.
//   ResolvedFact(string Feature, string Value, IReadOnlyList<ResolvedComponent> Components, string Confidence);
//   ResolvedComponent(string Label, string Value, ProvenanceRef? Provenance).
// SavingThrowProficiencies.cs = the static-class-data pattern to mirror for ArmorCatalog.
// CompanionUI/Pages/Campaigns/HeroDetail.razor: edit form uses `<div class="edit-grid">` with `<label>Name <input @bind="_editSheet.X" /></label>`;
//   the AC input is `<label>AC <input type="number" @bind="_editSheet.ArmorClass" /></label>` (~line 260).
//   _editSheet is a JSON deep-clone: `_editSheet = JsonSerializer.Deserialize<CharacterSheet>(JsonSerializer.Serialize(src))!;` (~line 470).
```

---

## Task 1: `WornArmor` field on `CharacterSheet`

**Files:**
- Modify: `Domain/CharacterSheet.cs` (new `WornArmor` property + `WornArmor` type)
- Test: `DndMcpAICsharpFun.Tests/` (a `CharacterSheet` serialization test — find where sheet (de)serialization is tested, e.g. near the legacy-migration tests)

**Interfaces:**
- Produces: `CharacterSheet.WornArmor { get; set; } = new();` and `public sealed class WornArmor { string ArmorName=""; bool Shield; int MagicBonus; }`.

- [ ] **Step 1: Write failing test** — a legacy `CharacterSheet` JSON with NO `WornArmor` key deserializes to a non-null unarmored default. Find the existing sheet-deserialization tests via Serena for style (there are legacy `Class`/`Level` sink tests).
```csharp
    [Fact]
    public void Legacy_sheet_without_worn_armor_deserializes_to_unarmored_default()
    {
        const string json = """{ "Race": "Elf", "Strength": 10, "Dexterity": 14 }""";
        var sheet = System.Text.Json.JsonSerializer.Deserialize<CharacterSheet>(json)!;
        sheet.WornArmor.Should().NotBeNull();
        sheet.WornArmor.ArmorName.Should().BeEmpty();
        sheet.WornArmor.Shield.Should().BeFalse();
    }
```

- [ ] **Step 2: Run → FAIL** (no `WornArmor` member).
Run: `dotnet test --filter "FullyQualifiedName~Legacy_sheet_without_worn_armor"` (dangerouslyDisableSandbox). Expected: does not compile / member missing.

- [ ] **Step 3: Implement** — Serena `insert_after_symbol` (after an existing property region) / `insert_before_symbol` on the trailing types. Add to `CharacterSheet`:
```csharp
    public WornArmor WornArmor { get; set; } = new();
```
and the type (alongside `CharacterFeature`/`ClassLevel`):
```csharp
public sealed class WornArmor
{
    /// <summary>PHB armor name (see ArmorCatalog); empty or "None" = unarmored.</summary>
    public string ArmorName { get; set; } = "";
    public bool Shield { get; set; }
    public int MagicBonus { get; set; }
}
```

- [ ] **Step 4: Run → PASS.** Build 0/0. Confirm NO migration is needed: `git status` shows no new file under `Migrations/`. Format the file.
- [ ] **Step 5: Commit:** `feat(domain): structured WornArmor on CharacterSheet (JSON, no migration)`

---

## Task 2: `ArmorCatalog` (static PHB armor data)

**Files:**
- Create: `Features/Resolution/ArmorCatalog.cs`
- Test: `DndMcpAICsharpFun.Tests/Resolution/ArmorCatalogTests.cs`

**Interfaces:**
- Produces: `enum ArmorCategory { Light, Medium, Heavy }`; `ArmorCatalog.Lookup(string name) : (int BaseAc, ArmorCategory Category)?`; `ArmorCatalog.Names : IReadOnlyList<string>` (dropdown order).

- [ ] **Step 1: Write failing tests.**
```csharp
using DndMcpAICsharpFun.Features.Resolution;
using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Resolution;

public sealed class ArmorCatalogTests
{
    [Fact]
    public void Lookup_returns_base_ac_and_category()
    {
        ArmorCatalog.Lookup("Chain Mail").Should().Be((16, ArmorCategory.Heavy));
        ArmorCatalog.Lookup("Leather").Should().Be((11, ArmorCategory.Light));
        ArmorCatalog.Lookup("Half Plate").Should().Be((15, ArmorCategory.Medium));
    }

    [Fact]
    public void Lookup_is_case_insensitive()
        => ArmorCatalog.Lookup("chain mail").Should().Be((16, ArmorCategory.Heavy));

    [Fact]
    public void Lookup_unknown_is_null()
        => ArmorCatalog.Lookup("Mithral Plate").Should().BeNull();

    [Fact]
    public void Names_lists_the_catalog_for_the_dropdown()
        => ArmorCatalog.Names.Should().Contain("Plate").And.Contain("Leather").And.HaveCount(12);
}
```

- [ ] **Step 2: Run → FAIL** (type missing).
- [ ] **Step 3: Implement** `Features/Resolution/ArmorCatalog.cs`:
```csharp
namespace DndMcpAICsharpFun.Features.Resolution;

public enum ArmorCategory { Light, Medium, Heavy }

/// <summary>
/// Static PHB armor data: name → (base AC, category). Single source of truth for armor base AC — a
/// character stores only the armor NAME (see <see cref="Domain.WornArmor"/>); the resolver looks up
/// base AC + category here. Same static-data pattern as SavingThrowProficiencies.
/// </summary>
public static class ArmorCatalog
{
    private static readonly Dictionary<string, (int BaseAc, ArmorCategory Category)> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Padded"] = (11, ArmorCategory.Light),
        ["Leather"] = (11, ArmorCategory.Light),
        ["Studded Leather"] = (12, ArmorCategory.Light),
        ["Hide"] = (12, ArmorCategory.Medium),
        ["Chain Shirt"] = (13, ArmorCategory.Medium),
        ["Scale Mail"] = (14, ArmorCategory.Medium),
        ["Breastplate"] = (14, ArmorCategory.Medium),
        ["Half Plate"] = (15, ArmorCategory.Medium),
        ["Ring Mail"] = (14, ArmorCategory.Heavy),
        ["Chain Mail"] = (16, ArmorCategory.Heavy),
        ["Splint"] = (17, ArmorCategory.Heavy),
        ["Plate"] = (18, ArmorCategory.Heavy),
    };

    // Explicit dropdown order (light → medium → heavy), independent of dictionary iteration order.
    public static IReadOnlyList<string> Names { get; } =
    [
        "Padded", "Leather", "Studded Leather",
        "Hide", "Chain Shirt", "Scale Mail", "Breastplate", "Half Plate",
        "Ring Mail", "Chain Mail", "Splint", "Plate",
    ];

    public static (int BaseAc, ArmorCategory Category)? Lookup(string name) =>
        Map.TryGetValue(name, out var v) ? v : null;
}
```

- [ ] **Step 4: Run → PASS.** Build 0/0. Format.
- [ ] **Step 5: Commit:** `feat(resolution): static ArmorCatalog (PHB armor base AC + category)`

---

## Task 3: `armor class` resolver (pure)

**Files:**
- Modify: `Features/Resolution/CharacterResolutionService.cs` (`ResolveArmorClass` static + `UnarmoredDefense` helper + dispatch branch)
- Test: `DndMcpAICsharpFun.Tests/Resolution/ArmorClassResolutionTests.cs` (pure unit — no DB)

**Interfaces:**
- Produces: `public static ResolvedFact CharacterResolutionService.ResolveArmorClass(CharacterSheet sheet)`; dispatch on `"armor class"`.

- [ ] **Step 1: Write failing tests — ONE PER BRANCH.**
```csharp
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Resolution;
using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Resolution;

public sealed class ArmorClassResolutionTests
{
    private static CharacterSheet Sheet(string @class, int level, int dex = 10, int con = 10, int wis = 10,
        string armor = "", bool shield = false, int magic = 0)
    {
        var s = new CharacterSheet { Dexterity = dex, Constitution = con, Wisdom = wis };
        s.Classes = [new ClassLevel { Class = @class, Level = level }];
        s.WornArmor = new WornArmor { ArmorName = armor, Shield = shield, MagicBonus = magic };
        return s;
    }

    [Fact] public void Heavy_armor_ignores_dex() =>
        CharacterResolutionService.ResolveArmorClass(Sheet("Fighter", 1, dex: 16, armor: "Plate")).Value.Should().Be("18");

    [Fact] public void Medium_armor_caps_dex_at_2() =>
        CharacterResolutionService.ResolveArmorClass(Sheet("Cleric", 1, dex: 18, armor: "Half Plate")).Value.Should().Be("17");

    [Fact] public void Light_armor_adds_full_dex() =>
        CharacterResolutionService.ResolveArmorClass(Sheet("Rogue", 1, dex: 16, armor: "Leather")).Value.Should().Be("14");

    [Fact] public void Shield_and_magic_add_on_top()
    {
        var fact = CharacterResolutionService.ResolveArmorClass(Sheet("Fighter", 1, dex: 10, armor: "Chain Mail", shield: true, magic: 1));
        fact.Value.Should().Be("19"); // 16 + 2 + 1
        fact.Components.Should().Contain(c => c.Label == "shield").And.Contain(c => c.Label == "magic");
    }

    [Fact] public void Barbarian_unarmored_defense_with_shield()
    {
        // 10 + Dex(+2) + Con(+3) + shield(2) = 17
        CharacterResolutionService.ResolveArmorClass(Sheet("Barbarian", 3, dex: 14, con: 16, shield: true)).Value.Should().Be("17");
    }

    [Fact] public void Monk_unarmored_defense_suppressed_by_shield()
    {
        // Monk UD not allowed with a shield → 10 + Dex(+2) + shield(2) = 14
        CharacterResolutionService.ResolveArmorClass(Sheet("Monk", 3, dex: 14, wis: 16, shield: true)).Value.Should().Be("14");
    }

    [Fact] public void Multiclass_takes_higher_unarmored_defense()
    {
        // Barbarian/Monk, no shield: max(10+Dex+Con, 10+Dex+Wis). Con 16(+3) > Wis 12(+1) → Barbarian.
        var s = new CharacterSheet { Dexterity = 14, Constitution = 16, Wisdom = 12,
            Classes = [new ClassLevel { Class = "Barbarian", Level = 2 }, new ClassLevel { Class = "Monk", Level = 2 }],
            WornArmor = new WornArmor() };
        CharacterResolutionService.ResolveArmorClass(s).Value.Should().Be("15"); // 10+2+3
    }

    [Fact] public void Unknown_armor_is_needsReview() =>
        CharacterResolutionService.ResolveArmorClass(Sheet("Fighter", 1, armor: "Mithral Plate")).Confidence.Should().Be("needsReview");

    [Fact] public void Default_worn_armor_is_unarmored()
    {
        var s = new CharacterSheet { Dexterity = 12, Classes = [new ClassLevel { Class = "Wizard", Level = 1 }] }; // WornArmor defaults to new()
        CharacterResolutionService.ResolveArmorClass(s).Value.Should().Be("11"); // 10 + Dex(+1)
    }
}
```

- [ ] **Step 2: Run → FAIL** (method missing / `"armor class"` unsupported).
Run: `dotnet test --filter "FullyQualifiedName~ArmorClassResolution"`.

- [ ] **Step 3: Implement** — Serena-add after `ResolveSavingThrows`, plus the dispatch branch (`if (feature.Equals("armor class", StringComparison.OrdinalIgnoreCase)) return Task.FromResult(ResolveArmorClass(sheet));`):
```csharp
    // Best applicable Unarmored Defense base (WITHOUT shield) across the character's classes:
    // Barbarian 10+Dex+Con (shield allowed), Monk 10+Dex+Wis (only when no shield). null if none apply.
    private static int? UnarmoredDefense(CharacterSheet sheet, bool hasShield)
    {
        int? best = null;
        var dex = CharacterSheet.Modifier(sheet.Dexterity);
        foreach (var c in sheet.Classes)
        {
            int? ud = null;
            if (string.Equals(c.Class, "Barbarian", StringComparison.OrdinalIgnoreCase))
                ud = 10 + dex + CharacterSheet.Modifier(sheet.Constitution);
            else if (string.Equals(c.Class, "Monk", StringComparison.OrdinalIgnoreCase) && !hasShield)
                ud = 10 + dex + CharacterSheet.Modifier(sheet.Wisdom);
            if (ud is not null && (best is null || ud > best)) best = ud;
        }
        return best;
    }

    /// <summary>
    /// Armor Class from the structured worn armor: unarmored 10+Dex raised to the best Unarmored Defense;
    /// Light base+Dex, Medium base+min(Dex,2), Heavy base; +2 shield; +magic. Pure/computed (no DB, no
    /// provenance). Unknown armor name => needsReview (no fabricated base AC).
    /// </summary>
    public static ResolvedFact ResolveArmorClass(CharacterSheet sheet)
    {
        var armor = sheet.WornArmor ?? new WornArmor();
        var dex = CharacterSheet.Modifier(sheet.Dexterity);
        var components = new List<ResolvedComponent>();
        static string Signed(int n) => n >= 0 ? $"+{n}" : n.ToString();

        var unarmored = string.IsNullOrWhiteSpace(armor.ArmorName)
                        || string.Equals(armor.ArmorName, "None", StringComparison.OrdinalIgnoreCase);
        int baseAc;
        if (unarmored)
        {
            baseAc = 10 + dex;
            components.Add(new ResolvedComponent("base", "10", null));
            components.Add(new ResolvedComponent("dex", Signed(dex), null));
            var ud = UnarmoredDefense(sheet, armor.Shield);
            if (ud is not null && ud > baseAc)
            {
                baseAc = ud.Value;
                components.Add(new ResolvedComponent("unarmored defense", ud.Value.ToString(), null));
            }
        }
        else
        {
            var entry = ArmorCatalog.Lookup(armor.ArmorName);
            if (entry is null)
                return new ResolvedFact("armor class", "unknown armor", [], "needsReview");
            var (b, cat) = entry.Value;
            var dexPart = cat switch
            {
                ArmorCategory.Light => dex,
                ArmorCategory.Medium => Math.Min(dex, 2),
                _ => 0, // Heavy
            };
            baseAc = b + dexPart;
            components.Add(new ResolvedComponent("armor", $"{armor.ArmorName} {b}", null));
            if (cat != ArmorCategory.Heavy)
                components.Add(new ResolvedComponent("dex", Signed(dexPart), null));
        }

        var total = baseAc;
        if (armor.Shield) { total += 2; components.Add(new ResolvedComponent("shield", "+2", null)); }
        if (armor.MagicBonus != 0) { total += armor.MagicBonus; components.Add(new ResolvedComponent("magic", Signed(armor.MagicBonus), null)); }

        return new ResolvedFact("armor class", total.ToString(), components, "ok");
    }
```

- [ ] **Step 4: Run → PASS** (all branch tests). Build 0/0. Format.
- [ ] **Step 5: Commit:** `feat(resolution): armor class resolver (armor + dex-by-category + shield + magic + unarmored defense)`

---

## Task 4: Hero-editor armor controls (capture only)

**Files:**
- Modify: `CompanionUI/Pages/Campaigns/HeroDetail.razor`

- [ ] **Step 1: Add the controls** — Serena edit inside the edit-grid, right after the AC input (~line 260). If `HeroDetail.razor` lacks `@using DndMcpAICsharpFun.Features.Resolution`, add it at the top with the other `@using`s (or fully-qualify `ArmorCatalog`).
```razor
                    <label>Armor
                        <select @bind="_editSheet.WornArmor.ArmorName">
                            <option value="">None</option>
                            @foreach (var name in ArmorCatalog.Names)
                            {
                                <option value="@name">@name</option>
                            }
                        </select>
                    </label>
                    <label>Shield <input type="checkbox" @bind="_editSheet.WornArmor.Shield" /></label>
                    <label>Armor Magic Bonus <input type="number" @bind="_editSheet.WornArmor.MagicBonus" /></label>
```

- [ ] **Step 2: Ensure `_editSheet.WornArmor` is non-null on edit** — Serena edit the `BeginEdit` logic (~line 470, right after the `_editSheet = JsonSerializer.Deserialize…` line): add `_editSheet.WornArmor ??= new();` (defensive — the property initializer already covers a missing JSON key, but a persisted explicit `null` would otherwise NRE the binding).

- [ ] **Step 3: Presentational gate.** Build 0/0; FULL `dotnet test` stays green (behavior unchanged — a new field + capture-only markup). Grep the classes the new markup uses (`edit-grid` label pattern — reuses existing classes, no new class needed) against `wwwroot/app.css`. Then rebuild the app container (`docker compose up -d --build app`; re-login `test`/`test` — the rebuild drops the cookie) and Playwright-screenshot the hero edit form at desktop (~1280) AND mobile (~390), plus a `browser_evaluate` `document.documentElement.scrollWidth > clientWidth` overflow check. Capture screenshots inline (`browser_take_screenshot` `type:"jpeg"`, no filename).

- [ ] **Step 4: Commit:** `feat(ui): worn-armor controls in the hero editor (capture only)`

---

## Task 5: Gates
- [ ] **Step 1:** `dotnet build` 0/0; FULL `dotnet test` green; `dotnet format DndMcpAICsharpFun.slnx --include` the touched files; `git diff --stat` confined to `Domain/CharacterSheet.cs` + `Features/Resolution/*` + `CompanionUI/Pages/Campaigns/HeroDetail.razor` + tests; **NO migration file**; no `.http`/insomnia change.

---

## Self-Review notes
- Spec Req 1 (WornArmor field + old-snapshot default + manual-AC independence) → Task 1. Req 2 (ArmorCatalog + unknown→needsReview) → Task 2 + the Task-3 unknown test. Req 3 (deterministic AC: heavy/medium/light/shield/magic + Barbarian/Monk UD + multiclass-higher) → Task 3, one test per scenario.
- Pure resolver (no DB) → unit tests, no Docker — mirrors `ResolveSavingThrows`. UI is capture-only → presentational gate (screenshots), full suite stays green.
- No migration (JSON column) — Task 1 Step 4 + Task 5 explicitly assert no `Migrations/` file appears.
- Reuse: `CharacterSheet.Modifier`, `sheet.Classes`, `ResolvedFact`/`ResolvedComponent`, the `SavingThrowProficiencies` static-data pattern, the existing edit-grid `<label>` markup.
