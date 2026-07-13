# Crafting Calculator Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a deterministic crafting math engine (`CraftingMath`) plus an ownership-free `calculate_crafting` chat tool, so crafting time/cost is computed exactly from rule-grounded formulas instead of the LLM's fuzzy arithmetic.

**Architecture:** `Features/Crafting/CraftingMath` is a pure static class (mirrors the shipped `Features/Encounters/EncounterMath`) — no I/O, no DI, unit-testable against the published rules. Nonmagical crafting uses the corpus-verified XGE downtime formula (materials = ½ market value; workweeks = value ÷ 50). Magic-item crafting is the XGE "Magic Item Crafting Time and Cost" table encoded as a switch. The `calculate_crafting` chat tool (registered in `DndChatService`, ownership-free like `ask_rules`/`plan_downtime`) branches on market-value-vs-rarity, calls `CraftingMath` directly, and returns the exact numbers plus a fixed citation.

**Tech Stack:** .NET 10, C#, xunit + FluentAssertions. `Microsoft.Extensions.AI` `AIFunctionFactory` for the chat tool.

## Global Constraints

- Target `net10.0`, nullable enabled, implicit usings, **warnings-as-errors** (repo `Directory.Build.props`).
- **Grounding:** nonmagical formula is corpus-verified; the magic-item table values MUST match the ingested XGE (verify in Task 1) or, if Marker's table rendering hides the cells, be the published XGE table encoded + cited (the `EncounterMath` precedent), documented in a code comment.
- **No** migration, HTTP route, `.http`/`.insomnia` change, shared MCP key, or new retrieval service — this is pure math + one chat tool.
- The `calculate_crafting` tool is **ownership-free**: registered in the authenticated block but takes no `userId`/`campaignId`.
- Exact magic-item table cells (workweeks, gold): Common (1, 50), Uncommon (2, 200), Rare (10, 2000), VeryRare (25, 20000), Legendary (50, 100000).
- Nonmagical: `MaterialsGp = marketValueGp / 2` (integer); `TotalWorkweeks = marketValueGp / 50.0` (double); `PerCrafterWorkweeks = TotalWorkweeks / max(1, crafters)`; `Days = ceil(PerCrafterWorkweeks * 5)`. `marketValueGp <= 0` throws `ArgumentOutOfRangeException`; `crafters` clamped to ≥ 1.

---

### Task 1: CraftingMath deterministic engine

**Files:**
- Create: `Features/Crafting/Rarity.cs`
- Create: `Features/Crafting/CraftingResults.cs`
- Create: `Features/Crafting/CraftingMath.cs`
- Test: `Tests/.../Crafting/CraftingMathTests.cs` (place beside the existing `EncounterMath` tests — find that test project/namespace and match it)

**Interfaces:**
- Produces:
  - `public enum Rarity { Common, Uncommon, Rare, VeryRare, Legendary }`
  - `public record NonmagicalCraft(int MaterialsGp, double TotalWorkweeks, double PerCrafterWorkweeks, int Days)`
  - `public record MagicItemCraft(Rarity Rarity, int Workweeks, int GoldCostGp)`
  - `public static class CraftingMath` with:
    - `NonmagicalCraft CraftNonmagical(int marketValueGp, int crafters = 1)`
    - `MagicItemCraft CraftMagicItem(Rarity rarity)`
  - Namespace: `DndMcpAICsharpFun.Features.Crafting`

- [ ] **Step 1: Locate the EncounterMath test file** to match its test project, namespace, and style.

Run: `grep -rl "class EncounterMathTests\|EncounterMath" --include=*.cs Tests* test* 2>/dev/null || grep -rln "EncounterMath" --include=*Tests*.cs .`
Note the test project path and namespace for placing `CraftingMathTests.cs`.

- [ ] **Step 2: Write the failing tests** in `CraftingMathTests.cs` (match the located test project namespace):

```csharp
using DndMcpAICsharpFun.Features.Crafting;
using FluentAssertions;
using Xunit;

public class CraftingMathTests
{
    [Fact]
    public void CraftNonmagical_PlateArmor_1500gp()
    {
        var r = CraftingMath.CraftNonmagical(1500);
        r.MaterialsGp.Should().Be(750);
        r.TotalWorkweeks.Should().Be(30.0);
        r.PerCrafterWorkweeks.Should().Be(30.0);
        r.Days.Should().Be(150);
    }

    [Fact]
    public void CraftNonmagical_MultipleCrafters_DivideTime()
    {
        var r = CraftingMath.CraftNonmagical(1500, crafters: 3);
        r.MaterialsGp.Should().Be(750);
        r.TotalWorkweeks.Should().Be(30.0);
        r.PerCrafterWorkweeks.Should().Be(10.0);
        r.Days.Should().Be(50);
    }

    [Fact]
    public void CraftNonmagical_FractionalWorkweeks_Preserved()
    {
        var r = CraftingMath.CraftNonmagical(175);
        r.TotalWorkweeks.Should().Be(3.5);
        r.Days.Should().Be(18); // ceil(17.5)
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void CraftNonmagical_NonPositiveValue_Throws(int value)
    {
        var act = () => CraftingMath.CraftNonmagical(value);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void CraftNonmagical_ZeroCrafters_ClampedToOne()
    {
        var r = CraftingMath.CraftNonmagical(1500, crafters: 0);
        r.PerCrafterWorkweeks.Should().Be(30.0);
        r.Days.Should().Be(150);
    }

    [Theory]
    [InlineData(Rarity.Common, 1, 50)]
    [InlineData(Rarity.Uncommon, 2, 200)]
    [InlineData(Rarity.Rare, 10, 2000)]
    [InlineData(Rarity.VeryRare, 25, 20000)]
    [InlineData(Rarity.Legendary, 50, 100000)]
    public void CraftMagicItem_RarityTable(Rarity rarity, int workweeks, int gold)
    {
        var r = CraftingMath.CraftMagicItem(rarity);
        r.Rarity.Should().Be(rarity);
        r.Workweeks.Should().Be(workweeks);
        r.GoldCostGp.Should().Be(gold);
    }
}
```

- [ ] **Step 3: Run the tests, verify they fail** (types/methods missing).

Run: `dotnet test --filter FullyQualifiedName~CraftingMathTests` (use `dangerouslyDisableSandbox` — this repo's dotnet needs it for git-crypt).
Expected: FAIL (compile error / type not found).

- [ ] **Step 4: Create `Features/Crafting/Rarity.cs`:**

```csharp
namespace DndMcpAICsharpFun.Features.Crafting;

/// <summary>Magic-item rarity tiers used by the XGE crafting table.</summary>
public enum Rarity
{
    Common,
    Uncommon,
    Rare,
    VeryRare,
    Legendary
}
```

- [ ] **Step 5: Create `Features/Crafting/CraftingResults.cs`:**

```csharp
namespace DndMcpAICsharpFun.Features.Crafting;

/// <summary>Deterministic nonmagical crafting result (XGE downtime formula).</summary>
public record NonmagicalCraft(int MaterialsGp, double TotalWorkweeks, double PerCrafterWorkweeks, int Days);

/// <summary>Deterministic magic-item crafting result (XGE rarity table).</summary>
public record MagicItemCraft(Rarity Rarity, int Workweeks, int GoldCostGp);
```

- [ ] **Step 6: Create `Features/Crafting/CraftingMath.cs`:**

```csharp
namespace DndMcpAICsharpFun.Features.Crafting;

/// <summary>
/// Pure, table-driven crafting math (mirrors <see cref="Encounters.EncounterMath"/>): no I/O, no DI,
/// so it can be unit-tested against the published rules. Nonmagical crafting uses the XGE downtime
/// formula (materials = half market value; workweeks = market value / 50, workweek = 5 days).
/// Magic-item crafting is the XGE "Magic Item Crafting Time and Cost" table.
/// </summary>
public static class CraftingMath
{
    /// <summary>
    /// XGE nonmagical downtime crafting. Materials cost half the item's market value; total workweeks
    /// are the market value divided by 50, split evenly across crafters; a workweek is 5 days.
    /// </summary>
    public static NonmagicalCraft CraftNonmagical(int marketValueGp, int crafters = 1)
    {
        if (marketValueGp <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(marketValueGp), marketValueGp,
                "Market value must be positive.");
        }

        var effectiveCrafters = Math.Max(1, crafters);
        var materials = marketValueGp / 2;
        var totalWorkweeks = marketValueGp / 50.0;
        var perCrafter = totalWorkweeks / effectiveCrafters;
        var days = (int)Math.Ceiling(perCrafter * 5);
        return new NonmagicalCraft(materials, totalWorkweeks, perCrafter, days);
    }

    /// <summary>
    /// XGE "Magic Item Crafting Time and Cost" table (workweeks, gold). Values are the published XGE
    /// table; the table cells do not survive Marker's PDF table rendering, so they are encoded here and
    /// cited to XGE — the same precedent as EncounterMath's DMG tables.
    /// </summary>
    public static MagicItemCraft CraftMagicItem(Rarity rarity)
    {
        var (workweeks, gold) = rarity switch
        {
            Rarity.Common => (1, 50),
            Rarity.Uncommon => (2, 200),
            Rarity.Rare => (10, 2000),
            Rarity.VeryRare => (25, 20000),
            Rarity.Legendary => (50, 100000),
            _ => throw new ArgumentOutOfRangeException(nameof(rarity), rarity, "Unknown rarity.")
        };
        return new MagicItemCraft(rarity, workweeks, gold);
    }
}
```

- [ ] **Step 7: Corpus-verify the magic-item table** against the ingested XGE (best-effort).

Run: `grep -rli "xanathar\|xge" books/conversion-cache/ | head` to find the XGE conversion, then
`grep -io "magic item crafting[^\"]*\|legendary[^\"]\{0,60\}" <that file> | head`. If the table cells
surface, confirm each encoded `(workweeks, gold)` pair matches. If they don't (Marker table rendering),
the code comment on `CraftMagicItem` already records that these are the published XGE values encoded +
cited (the EncounterMath precedent). Note the outcome in the task report.

- [ ] **Step 8: Run the CraftingMath tests, verify they pass.**

Run: `dotnet test --filter FullyQualifiedName~CraftingMathTests` (dangerouslyDisableSandbox).
Expected: PASS (all facts/theories green).

- [ ] **Step 9: Run the FULL suite** to confirm no regression.

Run: `dotnet test` (dangerouslyDisableSandbox; Docker must be up for persistence tests).
Expected: all green.

- [ ] **Step 10: Commit.**

```bash
git add Features/Crafting/ <test file>
git commit -m "feat(crafting): deterministic CraftingMath (nonmagical + magic-item table)"
```

---

### Task 2: calculate_crafting chat tool

**Files:**
- Modify: `Features/Chat/DndChatService.cs` (register the tool in the authenticated block, alongside `ask_rules`/`plan_downtime` near line 203; add a `ParseRarity` helper beside `ParseEdition`/`ParseDifficulty` near line 319–330)
- Test: the chat-tool test file (find the existing tests that exercise `DndChatService`/its tools and match that pattern; if the tools are tested via the service's public tool-list surface, follow that)

**Interfaces:**
- Consumes: `CraftingMath.CraftNonmagical`, `CraftingMath.CraftMagicItem`, `Rarity` (Task 1)
- Produces: a `calculate_crafting` tool with params `(int? marketValue, string? rarity, int? crafters)`; a private `static Rarity? ParseRarity(string?)` helper (returns null for unknown/blank); a small serializable result shape (anonymous object or a private record) carrying the computed numbers + a `citation` string.

- [ ] **Step 1: Inspect the existing chat-tool tests** to learn how a tool's delegate is invoked/asserted (guard messages, result shape).

Run: `grep -rln "calculate_crafting\|plan_downtime\|ask_rules\|DndChatService" --include=*Tests*.cs .` then read the closest match. Match its style for Step 4's tests.

- [ ] **Step 2: Add the `ParseRarity` helper** near the other parse helpers (after `ParseDifficulty`, ~line 330) in `DndChatService.cs`:

```csharp
private static Rarity? ParseRarity(string? rarity) => rarity?.Trim().ToLowerInvariant() switch
{
    "common" => Rarity.Common,
    "uncommon" => Rarity.Uncommon,
    "rare" => Rarity.Rare,
    "very rare" or "veryrare" or "very-rare" => Rarity.VeryRare,
    "legendary" => Rarity.Legendary,
    _ => null
};
```

Add `using DndMcpAICsharpFun.Features.Crafting;` to the file's usings if not already present.

- [ ] **Step 3: Register the `calculate_crafting` tool** in the authenticated block, right after the `plan_downtime` registration (~line 216). Follow the `AIFunctionFactory.Create` pattern used by `ask_rules`:

```csharp
toolList.Add(AIFunctionFactory.Create(
    (int? marketValue, string? rarity, int? crafters) =>
    {
        var hasValue = marketValue.HasValue;
        var hasRarity = !string.IsNullOrWhiteSpace(rarity);
        if (hasValue == hasRarity)
        {
            return (object)new { error = "Supply exactly ONE of marketValue (nonmagical item) or rarity (magic item)." };
        }

        if (hasValue)
        {
            if (marketValue!.Value <= 0)
            {
                return new { error = "marketValue must be a positive gold amount." };
            }
            var n = CraftingMath.CraftNonmagical(marketValue.Value, crafters ?? 1);
            return new
            {
                kind = "nonmagical",
                materialsGp = n.MaterialsGp,
                totalWorkweeks = n.TotalWorkweeks,
                perCrafterWorkweeks = n.PerCrafterWorkweeks,
                days = n.Days,
                crafters = Math.Max(1, crafters ?? 1),
                citation = "Xanathar's Guide to Everything / PHB — Crafting (downtime)"
            };
        }

        var parsed = ParseRarity(rarity);
        if (parsed is null)
        {
            return new { error = "Unknown rarity. Use common, uncommon, rare, very rare, or legendary." };
        }
        var m = CraftingMath.CraftMagicItem(parsed.Value);
        return new
        {
            kind = "magic-item",
            rarity = m.Rarity.ToString(),
            workweeks = m.Workweeks,
            goldCostGp = m.GoldCostGp,
            citation = "Xanathar's Guide to Everything — Crafting Magic Items"
        };
    },
    name: "calculate_crafting",
    description: "Calculate the EXACT time and cost to CRAFT an item. For a NONMAGICAL item pass its " +
        "marketValue (gold) and optionally crafters; for a MAGIC item pass its rarity (common/uncommon/" +
        "rare/very rare/legendary). Supply exactly ONE of marketValue or rarity. If you don't know the " +
        "item's price or rarity, look it up with search_entities FIRST. Returns deterministic numbers " +
        "(materials/gold cost, workweeks, days) and a rule citation — report these numbers and the " +
        "citation EXACTLY as returned; never re-derive the arithmetic or invent numbers. Not tied to any " +
        "campaign or character."));
```

> Note: if the located result shape convention (Step 1) uses a private record instead of anonymous objects, use that instead — but keep the same fields.

- [ ] **Step 4: Write failing tool-guard tests** matching the located pattern (Step 1). Cover: both `marketValue` and `rarity` supplied → error/guard; neither supplied → guard; `marketValue: 1500` → nonmagical numbers (materials 750, totalWorkweeks 30, days 150) + citation; `rarity: "rare"` → magic numbers (workweeks 10, gold 2000) + citation; `rarity: "bogus"` → guard. If the tools aren't directly unit-testable through the service surface, assert on `CraftingMath` for the numbers and add a focused delegate test only if the existing pattern supports it — otherwise fold the guard-branch coverage into the live smoke (Task 3) and note it in the report.

- [ ] **Step 5: Run the new tests, verify pass.**

Run: `dotnet test --filter <new test filter>` (dangerouslyDisableSandbox).
Expected: PASS.

- [ ] **Step 6: Run the FULL suite.**

Run: `dotnet test` (dangerouslyDisableSandbox).
Expected: all green.

- [ ] **Step 7: Commit.**

```bash
git add Features/Chat/DndChatService.cs <test file>
git commit -m "feat(crafting): calculate_crafting ownership-free chat tool"
```

---

### Task 3: Live smoke + finish

**Files:**
- Modify (only if the smoke reveals a lesson): `.claude/skills/dev-flow/SKILL.md`

- [ ] **Step 1: Live smoke** against the running host chat (start it if needed: `dotnet run`, dangerouslyDisableSandbox). Ask the companion to compute crafting for **plate armor** — expect it to invoke `calculate_crafting` and report **750 gp materials / 30 workweeks / 150 days** (contrast the v1 downtime smoke's wrong "30 days / 1,200 gp"). Then ask for a **rare magic item** — expect **10 workweeks / 2,000 gp**. Confirm the tool is actually invoked (logs) and the numbers are exact.

- [ ] **Step 2:** If the smoke surfaces a durable lesson (e.g. "deterministic-math tools beat LLM inline arithmetic for numeric answers; prefer single-param-per-branch tool shapes for qwen3 reliability"), add it to `.claude/skills/dev-flow/SKILL.md` and commit. Otherwise note the clean smoke in the task report and finish.
