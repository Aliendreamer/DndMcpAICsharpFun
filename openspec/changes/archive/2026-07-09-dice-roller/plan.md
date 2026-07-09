# Dice Roller (Item A) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax.

**Goal:** A dice roller on the campaign page — parse `NdX±K` (+ d20 advantage/disadvantage), roll over an injected RNG, show the result + breakdown + a session-local recent-rolls list. Ephemeral (no persistence — that's Item B).

**Architecture:** Pure `Features/Dice/` core (`DiceExpression` parse, `DiceRoller.Roll` over `IRandomSource`, `RollResult` with breakdown) + a reusable Blazor `DiceRollerPanel` component embedded on `CampaignDetail`. RNG is the only injected nondeterminism → the whole core is deterministically unit-tested.

**Tech Stack:** .NET 10, Blazor Server (`InteractiveServer`), xUnit + FluentAssertions.

## Global Constraints

- `net10.0`; nullable; **warnings-as-errors**; Central Package Management. Use **Serena** for all `.cs`/`.razor` edits (every subagent prompt: CRITICAL-Serena block + `initial_instructions`). Run `dotnet` with `dangerouslyDisableSandbox: true`. Solution `.slnx`.
- Core is PURE except `IRandomSource` (the single injected nondeterminism) — table/deterministic tests, no flakiness.
- **No DB, no HTTP route, no MCP tool.** Rolls are ephemeral (in-component only). No `.http`/`.insomnia` change.
- Component `@inject`s the fully-qualified `DndMcpAICsharpFun.Features.Dice.DiceRoller` (name it **`DiceRollerPanel.razor`** so the component class doesn't collide with the service class `DiceRoller`). Components use inline `@code`, no `.razor.cs`. Follow `CampaignDetail.razor`'s style (`@rendermode InteractiveServer`, existing `app.css` classes).
- Supported dice: {4,6,8,10,12,20,100}. Count 1..`MaxCount` (const 100). Adv/dis only when `Die==20 && Count==1`.

---

### Task 1: DiceExpression parser (pure, TDD)

**Files:**
- Create: `Features/Dice/DiceExpression.cs`
- Test: `DndMcpAICsharpFun.Tests/Dice/DiceExpressionTests.cs`

**Produces (consumed by Task 2/3):**
- `enum RollMode { Normal, Advantage, Disadvantage }`
- `readonly record struct DiceExpression(int Count, int Die, int Modifier, RollMode Mode)` with:
  - `const int MaxCount = 100;` `static readonly int[] Dice = {4,6,8,10,12,20,100};`
  - `static bool TryParse(string input, out DiceExpression expr, out string? error)` — NEVER throws.
  - `static DiceExpression Parse(string input)` — throws `FormatException(error)` (thin wrapper over TryParse).
  - Grammar: `^\s*(\d+)?d(4|6|8|10|12|20|100)\s*([+-]\s*\d+)?\s*(adv|dis|advantage|disadvantage)?\s*$` (case-insensitive) — or a manual parse. Count default 1; modifier default 0; adv/dis → Mode, valid only when Die==20 && Count==1 (else error). Count>MaxCount or <1 → error.

- [ ] **Step 1: Write the failing tests**

```csharp
using DndMcpAICsharpFun.Features.Dice;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Dice;

public sealed class DiceExpressionTests
{
    [Fact] public void Parses_count_die_modifier()
    {
        DiceExpression.TryParse("2d6+3", out var e, out var err).Should().BeTrue();
        err.Should().BeNull();
        e.Should().Be(new DiceExpression(2, 6, 3, RollMode.Normal));
    }

    [Fact] public void Bare_die_defaults_count_1_mod_0()
    {
        DiceExpression.TryParse("d20", out var e, out _).Should().BeTrue();
        e.Should().Be(new DiceExpression(1, 20, 0, RollMode.Normal));
    }

    [Theory]
    [InlineData("1d20-1", 1, 20, -1)]
    [InlineData("4d8", 4, 8, 0)]
    [InlineData("1d100+10", 1, 100, 10)]
    public void Parses_variants(string s, int c, int d, int m)
    {
        DiceExpression.TryParse(s, out var e, out _).Should().BeTrue();
        e.Should().Be(new DiceExpression(c, d, m, RollMode.Normal));
    }

    [Fact] public void Advantage_on_single_d20_ok()
    {
        DiceExpression.TryParse("d20 adv", out var e, out _).Should().BeTrue();
        e.Mode.Should().Be(RollMode.Advantage);
    }

    [Theory]
    [InlineData("1d7")]           // unsupported die
    [InlineData("2d20 adv")]      // adv on multiple
    [InlineData("1d6 adv")]       // adv on non-d20
    [InlineData("999d100")]       // over MaxCount
    [InlineData("0d6")]           // count < 1
    [InlineData("hello")]         // garbage
    [InlineData("")]              // empty
    public void Invalid_inputs_are_rejected_without_throwing(string s)
    {
        DiceExpression.TryParse(s, out _, out var err).Should().BeFalse();
        err.Should().NotBeNullOrEmpty();
    }
}
```

- [ ] **Step 2: Run — verify fail.** `dotnet test --filter FullyQualifiedName~DiceExpressionTests` → FAIL (type missing).
- [ ] **Step 3: Implement `DiceExpression`** (regex or manual TryParse; Parse wraps it).
- [ ] **Step 4: Run — pass; `dotnet build` 0/0.**
- [ ] **Step 5: Commit** `feat(dice): DiceExpression parser for NdX±K + advantage/disadvantage`.

---

### Task 2: IRandomSource + DiceRoller + RollResult (TDD)

**Files:**
- Create: `Features/Dice/IRandomSource.cs`, `Features/Dice/RollResult.cs`, `Features/Dice/DiceRoller.cs`
- Modify: `Extensions/ServiceCollectionExtensions.cs` (register `IRandomSource` + `DiceRoller`)
- Test: `DndMcpAICsharpFun.Tests/Dice/DiceRollerTests.cs`

**Produces:**
- `interface IRandomSource { int Next(int minInclusive, int maxExclusive); }`; `sealed class SystemRandomSource : IRandomSource` (wraps `Random.Shared` or a `Random`).
- `sealed record RollResult(DiceExpression Expression, IReadOnlyList<int> Dice, IReadOnlyList<int> Kept, int Modifier, int Total, string Breakdown)`.
- `sealed class DiceRoller(IRandomSource rng)` with `RollResult Roll(DiceExpression e)`:
  - Normal: roll `Count` dice, each `rng.Next(1, Die+1)`; `Kept = Dice`; `Total = sum(Dice) + Modifier`.
  - Advantage/Disadvantage: roll two d20 (`Dice = [a,b]`), `kept = Mode==Advantage ? max : min`; `Kept = [kept]`; `Total = kept + Modifier`.
  - Breakdown: Normal → `$"{Count}d{Die}{ModStr} → [{join Dice}]{ModStr} = {Total}"` (omit `[..]` mod when Count==1? keep simple: always show dice list + mod). Adv/dis → `$"d20 ({advOrDis}) → [{a},{b}] → {kept}{ModStr}= {Total}"`. Match the exact expected strings in the tests below; adjust the format to make the tests pass (tests are the spec for the string).

- [ ] **Step 1: Write the failing tests** (scripted RNG)

```csharp
using DndMcpAICsharpFun.Features.Dice;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Dice;

public sealed class DiceRollerTests
{
    // Returns scripted values in order; Next(min,max) returns next script value (asserted in range by the test cases).
    private sealed class ScriptedRng(params int[] vals) : IRandomSource
    {
        private int _i;
        public int Next(int min, int max) => vals[_i++];
    }

    [Fact] public void Rolls_within_range_with_real_rng()
    {
        var r = new DiceRoller(new SystemRandomSource());
        var res = r.Roll(DiceExpression.Parse("3d6"));
        res.Dice.Should().HaveCount(3);
        res.Dice.Should().OnlyContain(v => v >= 1 && v <= 6);
    }

    [Fact] public void Modifier_applied_and_dice_reported()
    {
        var r = new DiceRoller(new ScriptedRng(4, 5));
        var res = r.Roll(DiceExpression.Parse("2d6+3"));
        res.Dice.Should().Equal(4, 5);
        res.Total.Should().Be(12);
        res.Breakdown.Should().Be("2d6+3 → [4,5]+3 = 12");
    }

    [Fact] public void Advantage_keeps_higher()
    {
        var r = new DiceRoller(new ScriptedRng(18, 7));
        var res = r.Roll(DiceExpression.Parse("d20 adv"));
        res.Dice.Should().Equal(18, 7);
        res.Kept.Should().Equal(18);
        res.Total.Should().Be(18);
        res.Breakdown.Should().Be("d20 (adv) → [18,7] → 18");
    }

    [Fact] public void Disadvantage_keeps_lower()
    {
        var r = new DiceRoller(new ScriptedRng(18, 7));
        var res = r.Roll(DiceExpression.Parse("d20 dis"));
        res.Kept.Should().Equal(7);
        res.Total.Should().Be(7);
        res.Breakdown.Should().Be("d20 (dis) → [18,7] → 7");
    }

    [Fact] public void Same_script_is_reproducible()
    {
        var a = new DiceRoller(new ScriptedRng(3, 3, 3)).Roll(DiceExpression.Parse("3d6"));
        var b = new DiceRoller(new ScriptedRng(3, 3, 3)).Roll(DiceExpression.Parse("3d6"));
        a.Should().BeEquivalentTo(b);
    }
}
```

- [ ] **Step 2: Run — verify fail.**
- [ ] **Step 3: Implement `IRandomSource`/`SystemRandomSource`, `RollResult`, `DiceRoller`.** Make the breakdown strings match the tests exactly (the `→` is U+2192). Register in DI: `services.AddSingleton<IRandomSource, SystemRandomSource>(); services.AddScoped<DiceRoller>();` (or a suitable group — put in a small `AddDice()` and call it from the composition, self-contained per the dev-flow DI gate).
- [ ] **Step 4: Run — pass; `dotnet build` 0/0.**
- [ ] **Step 5: Commit** `feat(dice): DiceRoller over injected IRandomSource + RollResult breakdown`.

---

### Task 3: DiceRollerPanel Blazor component on the campaign page

**Files:**
- Create: `CompanionUI/Components/DiceRollerPanel.razor`
- Modify: `CompanionUI/Pages/Campaigns/CampaignDetail.razor` (embed `<DiceRollerPanel />`)

**Component:**
- `@inject DndMcpAICsharpFun.Features.Dice.DiceRoller Roller` (fully-qualified — avoids the class-name collision).
- State (`@code`): the composed inputs (selected die, count, modifier, adv/dis toggle) OR a free-text `_expression` string; `_error` string; `List<RollResult> _recent = new();`.
- UI: quick-die buttons d4/d6/d8/d10/d12/d20/d100 (set the die / append to expression), a count stepper (min 1), a modifier `+/-` input, an adv/dis toggle **enabled only when die==20 && count==1**; a free-text expression field bound to `_expression`; a **Roll** button.
- `Roll()`: build the expression string (from the controls or the free-text field), `DiceExpression.TryParse(...)` → on false set `_error` and return (NO throw); else `var res = Roller.Roll(expr); _recent.Insert(0, res); if (_recent.Count > 10) _recent.RemoveAt(10); _error = null;`.
- Render: the latest result (total + `res.Breakdown`) prominently, then the `_recent` list (breakdowns), and `_error` when set. Use existing `app.css` classes / simple markup consistent with `CampaignDetail`.

- [ ] **Step 1: Create `DiceRollerPanel.razor`** per above (inline `@code`, no code-behind). Match the repo's Blazor style (look at `Chat.razor`/`CampaignDetail.razor` with Serena for markup + binding idioms — `@bind`, `@onclick`).
- [ ] **Step 2: Embed on `CampaignDetail.razor`** — add a `<section>`/panel with `<DiceRollerPanel />` in a sensible spot (e.g. below the campaign header / alongside notes). Confirm the `@using`/namespace resolves the component (components in `CompanionUI/Components` may need a `@using` in `_Imports.razor` — check and add if needed).
- [ ] **Step 3: Build** — `dotnet build` 0/0 (Blazor compiles the component + page; a binding/namespace error fails the build). Grep-confirm no DB/HTTP/MCP added.
- [ ] **Step 4: Commit** `feat(dice): DiceRollerPanel component embedded on the campaign page (ephemeral rolls)`.

---

### Task 4: Verify + review

- [ ] **Step 1: Full build 0/0 + full suite** green (incl. Testcontainers). `dotnet format "./DndMcpAICsharpFun.slnx" --verify-no-changes` — normalize the new files if it flags them (commit the format).
- [ ] **Step 2: Drive it (per `run`/`verify`)** — if the stack is up, open a campaign, roll `2d6+3`, `d20 adv`, and an invalid expression; confirm result/breakdown, recent-rolls list, and the error message (no crash). Defer honestly if no live run; the core unit tests carry correctness. (Playwright MCP: drive login + campaign + roll in ONE `browser_run_code_unsafe` call — the MCP browser context drops auth between tool calls; see roadmap Playwright gotcha.)
- [ ] **Step 3: Whole-branch review (opus)** — cross-check every ADDED requirement (parse valid/invalid, roll range, adv/dis keep, breakdown format, component ephemeral-no-persist). Confirm no persistence/HTTP/MCP leaked in. Address findings; then this slice (Item A) is done — proceed to Item B (its own brainstorm→spec→plan→implement).
