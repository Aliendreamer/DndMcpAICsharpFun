# Hero Multiclass Editing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the Blazor `HeroDetail` edit form multiclass-aware — a per-class list editor that preserves all classes on save, with live derived level/PB and a non-blocking validity + proficiency advisory.

**Architecture:** Add one public source-of-truth list (`MulticlassRules.KnownClasses`) for the class dropdown. Rewrite the `HeroDetail.razor` edit form so its class rows bind directly to `_editSheet.Classes` (the domain source of truth) — the same pattern the file already uses for its Features list — which removes the lossy `SetSingleClass` mapping entirely. Validity/proficiency are computed synchronously on the in-memory sheet via the already-tested `MulticlassRules` methods; no DB or MCP round-trip.

**Tech Stack:** .NET 10, C# (nullable, warnings-as-errors), Blazor Server (interactive components), xunit + FluentAssertions. The domain multiclass model + rules shipped in the archived `multiclass-character` change.

## Global Constraints

- **Target:** `net10.0`; nullable enabled; **warnings-as-errors** — a warning fails the build.
- **Serena for code edits** on existing files (`.razor`, `.cs`); built-in Read/Edit are not for code files here. Verify real newlines with `grep -n` after any Serena `replace_content` regex-mode edit.
- **`dotnet build`/`dotnet test` need `dangerouslyDisableSandbox: true`** (Config is git-crypted → sandboxed dotnet fails).
- **No bUnit** — the project has no Blazor component-test harness and this slice does not add one. The Razor is verified by build (warnings-as-errors) + a manual run smoke. Only `MulticlassRules.KnownClasses` gets a unit test.
- **No HTTP route change** → `DndMcpAICsharpFun.http` / `dnd-mcp-api.insomnia.json` are untouched.
- Tests: xunit + FluentAssertions; the test project global-imports `Xunit` but NOT `FluentAssertions` — add `using FluentAssertions;` explicitly. The repo's LSP emits false "Fact/Should/Classes not found" errors on test files — ignore them; `dotnet build`/`dotnet test` are ground truth.
- **Source-of-truth rule (from `multiclass-character`):** `CharacterSheet.Classes` is authoritative; `Class`/`Subclass`/`Level`/`ProficiencyBonus` are derived read-only getters. Edit the list, never the derived fields.

---

## File Structure

- `Features/Resolution/MulticlassRules.cs` — **modify**: add `public static readonly IReadOnlyList<string> KnownClasses`.
- `CompanionUI/Components/Pages/Campaigns/HeroDetail.razor` — **modify**: view-mode class line (43); edit grid (52–60); PB readout (136); `@code` fields (300–302); `EnterEdit` (338–344); `ConfirmSaveAsync` (364); add `AddClass`/`RemoveClass` helpers near `AddFeature` (359). Add `@using DndMcpAICsharpFun.Features.Resolution` if not already imported.
- `DndMcpAICsharpFun.Tests/Resolution/MulticlassRulesTests.cs` — **modify**: add a `KnownClasses` test (sibling of the existing `MulticlassRulesTests`).

---

## Task 1: `MulticlassRules.KnownClasses` — the dropdown source

**Files:**
- Modify: `Features/Resolution/MulticlassRules.cs`
- Test: `DndMcpAICsharpFun.Tests/Resolution/MulticlassRulesTests.cs`

**Interfaces:**
- Produces: `MulticlassRules.KnownClasses : IReadOnlyList<string>` — the 13 class names, each a valid key for `CanMulticlassInto` / `MulticlassProficiencies`.

- [ ] **Step 1: Write the failing test**

Add to `DndMcpAICsharpFun.Tests/Resolution/MulticlassRulesTests.cs` (the file already exists with `using FluentAssertions;` and namespace `DndMcpAICsharpFun.Tests.Resolution`):

```csharp
    [Fact]
    public void KnownClasses_has_the_13_classes_and_every_entry_is_a_valid_rules_key()
    {
        MulticlassRules.KnownClasses.Should().HaveCount(13);
        MulticlassRules.KnownClasses.Should().BeEquivalentTo(new[]
        {
            "Barbarian", "Bard", "Cleric", "Druid", "Fighter", "Monk", "Paladin",
            "Ranger", "Rogue", "Sorcerer", "Warlock", "Wizard", "Artificer",
        });

        var sheet = new CharacterSheet(); // all abilities default 0 — prereqs fail, but the class is KNOWN
        foreach (var c in MulticlassRules.KnownClasses)
        {
            // A known class never yields the "Unknown class" reason, and always has a proficiency entry.
            MulticlassRules.CanMulticlassInto(c, sheet).Reason.Should().NotContain("Unknown class");
            MulticlassRules.MulticlassProficiencies(c).Should().NotBeNull();
        }
    }
```

The test references `CharacterSheet` (namespace `DndMcpAICsharpFun.Domain`) and `MulticlassRules` (`DndMcpAICsharpFun.Features.Resolution`) — confirm those `using`s are present at the top of the test file (the existing tests already use both); add whichever is missing.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~MulticlassRulesTests` (with `dangerouslyDisableSandbox: true`)
Expected: FAIL — `MulticlassRules` does not contain `KnownClasses`.

- [ ] **Step 3: Add `KnownClasses` to `MulticlassRules.cs`**

Add this public member to the `MulticlassRules` static class (place it just above `CanMulticlassInto`). It is an explicit ordered list; its contents must equal the keys of the `Prereqs`/`ProficiencySubsets` maps:

```csharp
    /// <summary>
    /// The 13 supported class names — the single source for the hero-editor class dropdown. Kept identical
    /// to the prerequisite/proficiency map keys (unit-tested), so the UI can never offer a name the rules
    /// engine does not understand.
    /// </summary>
    public static readonly IReadOnlyList<string> KnownClasses =
    [
        "Barbarian", "Bard", "Cleric", "Druid", "Fighter", "Monk", "Paladin",
        "Ranger", "Rogue", "Sorcerer", "Warlock", "Wizard", "Artificer",
    ];
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter FullyQualifiedName~MulticlassRulesTests` (dangerouslyDisableSandbox)
Expected: PASS.

- [ ] **Step 5: Build + commit**

Run: `dotnet build` (dangerouslyDisableSandbox) → 0/0.

```bash
git add Features/Resolution/MulticlassRules.cs DndMcpAICsharpFun.Tests/Resolution/MulticlassRulesTests.cs
git commit -m "feat(rules): expose MulticlassRules.KnownClasses (dropdown source)"
```

---

## Task 2: Per-class list editor + footgun fix

**Files:**
- Modify: `CompanionUI/Components/Pages/Campaigns/HeroDetail.razor`

**Interfaces:**
- Consumes: `MulticlassRules.KnownClasses` (Task 1); `CharacterSheet.Classes` / `ClassLevel` (Domain); `CharacterSheet.ProficiencyBonusForLevel(int)` (existing).
- Produces: `AddClass()` / `RemoveClass(int)` handlers; the edit form binds class rows directly to `_editSheet.Classes`.

There is no automated component test (no bUnit). The deliverable is verified by build (warnings-as-errors) + the Task 4 manual smoke. Keep the change mechanical and mirror the existing Features-list editor.

- [ ] **Step 1: Ensure the Resolution namespace is imported**

At the top of `HeroDetail.razor`, confirm/add: `@using DndMcpAICsharpFun.Features.Resolution` (needed for `MulticlassRules`). `CharacterSheet`/`ClassLevel` (namespace `DndMcpAICsharpFun.Domain`) are already in scope (the file already constructs `new CharacterSheet()`); if the Domain `@using` isn't present, add it too.

- [ ] **Step 2: Replace the three single-class inputs in the edit grid (lines 54–56)**

Current (lines 52–60):

```razor
                <div class="edit-grid">
                    <label>Race <input @bind="_editSheet.Race" /></label>
                    <label>Class <input @bind="_editClass" /></label>
                    <label>Subclass <input @bind="_editSubclass" /></label>
                    <label>Level <input type="number" @bind="_editLevel" /></label>
                    <label>Background <input @bind="_editSheet.Background" /></label>
                    <label>Alignment <input @bind="_editSheet.Alignment" /></label>
                    <label>XP <input type="number" @bind="_editSheet.ExperiencePoints" /></label>
                </div>
```

Replace the three `Class`/`Subclass`/`Level` `<label>` lines with a class-list editor (mirrors the Features `@for` at lines ~220–229). Keep `Race`, `Background`, `Alignment`, `XP`:

```razor
                <div class="edit-grid">
                    <label>Race <input @bind="_editSheet.Race" /></label>
                    <div class="class-rows">
                        <b>Classes</b>
                        @for (int i = 0; i < _editSheet.Classes.Count; i++)
                        {
                            var idx = i;
                            <div class="class-edit-row">
                                <select @bind="_editSheet.Classes[idx].Class">
                                    @foreach (var cls in MulticlassRules.KnownClasses)
                                    {
                                        <option value="@cls">@cls</option>
                                    }
                                </select>
                                <input type="number" min="1" @bind="_editSheet.Classes[idx].Level" />
                                <input placeholder="Subclass" @bind="_editSheet.Classes[idx].Subclass" />
                                <button type="button" @onclick="() => RemoveClass(idx)">Remove</button>
                            </div>
                        }
                        <button type="button" @onclick="AddClass">+ Add class</button>
                        <span class="derived-readout">
                            <b>Total level:</b> @_editSheet.Level &nbsp;
                            <b>Prof. Bonus:</b> +@CharacterSheet.ProficiencyBonusForLevel(_editSheet.Level)
                        </span>
                    </div>
                    <label>Background <input @bind="_editSheet.Background" /></label>
                    <label>Alignment <input @bind="_editSheet.Alignment" /></label>
                    <label>XP <input type="number" @bind="_editSheet.ExperiencePoints" /></label>
                </div>
```

- [ ] **Step 3: Fix the combat-section PB readout (line 136)**

It currently reads `_editLevel`, which is being removed. Change line 136 from:

```razor
                    <span class="prof-bonus-readout"><b>Prof. Bonus:</b> +@CharacterSheet.ProficiencyBonusForLevel(_editLevel) (auto, from level)</span>
```

to derive from the sheet's total level:

```razor
                    <span class="prof-bonus-readout"><b>Prof. Bonus:</b> +@CharacterSheet.ProficiencyBonusForLevel(_editSheet.Level) (auto, from level)</span>
```

- [ ] **Step 4: Remove the three locals and their initialization**

In the `@code` block, delete these three fields (lines 300–302):

```csharp
    private string _editClass = "";
    private string _editSubclass = "";
    private int _editLevel;
```

In `EnterEdit` (lines 342–344), delete the three initializer lines:

```csharp
        _editClass = _editSheet.Class;
        _editSubclass = _editSheet.Subclass;
        _editLevel = _editSheet.Level;
```

(The `_editSheet = JsonSerializer.Deserialize<CharacterSheet>(JsonSerializer.Serialize(src))!;` deep-copy on the line above already carries `Classes`, so the rows populate from it.)

- [ ] **Step 5: Drop the `SetSingleClass` call in `ConfirmSaveAsync` (the footgun fix)**

In `ConfirmSaveAsync` (line 364), delete:

```csharp
        _editSheet.SetSingleClass(_editClass, _editSubclass, _editLevel);
```

`_editSheet.Classes` is already the edited source of truth, so nothing replaces it. The remaining `Split(...)` assignments stay.

- [ ] **Step 6: Add `AddClass` / `RemoveClass` handlers**

Next to `AddFeature` (line 359: `private void AddFeature() => _editSheet.Features.Add(new CharacterFeature());`), add:

```csharp
    private void AddClass() =>
        _editSheet.Classes.Add(new ClassLevel { Class = MulticlassRules.KnownClasses[0], Level = 1 });
    private void RemoveClass(int index) => _editSheet.Classes.RemoveAt(index);
```

(New rows default to a valid `<select>` option and level 1, so the bound `<select>` never starts on an out-of-list value.)

- [ ] **Step 7: Build**

Run: `dotnet build` (dangerouslyDisableSandbox)
Expected: 0 warnings / 0 errors. (Warnings-as-errors surfaces any leftover reference to the deleted `_editClass`/`_editSubclass`/`_editLevel` as a compile error — grep `HeroDetail.razor` for those names and confirm none remain.)

- [ ] **Step 8: Commit**

```bash
git add CompanionUI/Components/Pages/Campaigns/HeroDetail.razor
git commit -m "feat(ui): per-class list editor in HeroDetail; drop SetSingleClass collapse"
```

---

## Task 3: Non-blocking validity + proficiency advisory

**Files:**
- Modify: `CompanionUI/Components/Pages/Campaigns/HeroDetail.razor`

**Interfaces:**
- Consumes: `MulticlassRules.CanMulticlassInto(string, CharacterSheet)` → `PrereqResult(bool Allowed, string Reason)`; `MulticlassRules.MulticlassProficiencies(string)` → `IReadOnlyList<string>` (both already shipped + tested).

- [ ] **Step 1: Add the advisory inside each class row**

In the class-edit row markup from Task 2, inside the `<div class="class-edit-row">` (after the Remove button), add a per-row advisory. The prerequisite advisory shows only for **non-primary** rows (`idx > 0`); the proficiency subset shows for every row:

```razor
                            <div class="class-edit-row">
                                <select @bind="_editSheet.Classes[idx].Class">
                                    @foreach (var cls in MulticlassRules.KnownClasses)
                                    {
                                        <option value="@cls">@cls</option>
                                    }
                                </select>
                                <input type="number" min="1" @bind="_editSheet.Classes[idx].Level" />
                                <input placeholder="Subclass" @bind="_editSheet.Classes[idx].Subclass" />
                                <button type="button" @onclick="() => RemoveClass(idx)">Remove</button>
                                @if (idx > 0)
                                {
                                    var check = MulticlassRules.CanMulticlassInto(_editSheet.Classes[idx].Class, _editSheet);
                                    <span class="advisory @(check.Allowed ? "advisory-ok" : "advisory-warn")">
                                        @(check.Allowed ? "✓ multiclass allowed" : $"⚠ {check.Reason}")
                                    </span>
                                }
                                <span class="advisory advisory-prof">
                                    @{
                                        var profs = MulticlassRules.MulticlassProficiencies(_editSheet.Classes[idx].Class);
                                    }
                                    @(profs.Count == 0 ? "" : $"grants: {string.Join(", ", profs)}")
                                </span>
                            </div>
```

The advisory is pure display: it reads the current `_editSheet` ability scores and recomputes on each render (Blazor re-renders on any bound edit). It never disables the Save button or gates `ConfirmSaveAsync` — leave the Save flow untouched.

- [ ] **Step 2: Build**

Run: `dotnet build` (dangerouslyDisableSandbox)
Expected: 0/0. `CanMulticlassInto` returns a record with `Allowed`/`Reason`; `MulticlassProficiencies` returns a list — both used read-only.

- [ ] **Step 3: Confirm save is never blocked**

Grep `HeroDetail.razor` for the Save button (the `ConfirmSaveAsync`/`ShowSavePrompt` trigger) and confirm no `disabled=` was added tied to the advisory, and `ConfirmSaveAsync` has no prereq guard. State this in the report.

- [ ] **Step 4: Commit**

```bash
git add CompanionUI/Components/Pages/Campaigns/HeroDetail.razor
git commit -m "feat(ui): non-blocking multiclass validity + proficiency advisory in editor"
```

---

## Task 4: View-mode all-classes + validation

**Files:**
- Modify: `CompanionUI/Components/Pages/Campaigns/HeroDetail.razor`

- [ ] **Step 1: List all classes in view mode (line 43)**

Current:

```razor
                    <span><b>Class:</b> @sheet.Class @(string.IsNullOrEmpty(sheet.Subclass) ? "" : $"({sheet.Subclass})")</span>
```

Replace with a rendering of every class entry (falls back to "—" when class-less):

```razor
                    <span><b>Class:</b> @(sheet.Classes.Count == 0
                        ? "—"
                        : string.Join(" / ", sheet.Classes.Select(c =>
                            string.IsNullOrEmpty(c.Subclass) ? $"{c.Class} {c.Level}" : $"{c.Class} {c.Level} ({c.Subclass})")))</span>
```

`@sheet.Level` on line 44 already shows the total — leave it. If `System.Linq` is not already imported in the razor (needed for `.Select`), add `@using System.Linq` at the top (Blazor `_Imports.razor` usually includes it — confirm, and only add if missing).

- [ ] **Step 2: Build + full non-persistence suite**

Run: `dotnet build` (dangerouslyDisableSandbox) → 0/0.
Run: `dotnet test --filter "FullyQualifiedName!~Persistence"` (dangerouslyDisableSandbox) → green (includes the new `KnownClasses` test).

- [ ] **Step 3: Manual UI smoke (documented, not automated)**

There is no bUnit; verify by running the app (`dotnet run`, then the hero page) OR, if a live run isn't available in this environment, state that explicitly and rely on the build + the logic being tested domain-side. Check:
- A single-class hero opens with exactly one class row.
- "+ Add class" adds a row; Total level + Prof. Bonus update live; the 2nd row shows the validity + proficiency advisory.
- Removing a row updates the totals; removing all rows shows "—" in view mode and does not crash.
- Editing a genuine two-class hero and saving preserves BOTH classes (no collapse) — this is the footgun fix.

- [ ] **Step 4: Docs check**

No HTTP route changed → `.http`/`.insomnia` untouched. `grep -rn "HeroDetail\|hero editor" docs README.md CLAUDE.md`; update only if the hero editor's class-editing behavior is documented (likely a no-op — state it).

- [ ] **Step 5: Commit**

```bash
git add CompanionUI/Components/Pages/Campaigns/HeroDetail.razor
git commit -m "feat(ui): list all classes in HeroDetail view mode"
```

---

## Self-Review

**Spec coverage** (each ADDED requirement → task):
- *The class-name list is a single public source* → Task 1 (`KnownClasses` + test). ✓
- *The hero editor edits a per-class list without collapsing classes* → Task 2 (list editor bound to `Classes`; `SetSingleClass` removed; add/remove). ✓
- *The editor shows live derived level and proficiency bonus* → Task 2 (Total level + PB readouts from `_editSheet`). ✓
- *Non-blocking multiclass validity and proficiency advisory* → Task 3 (per non-primary row `CanMulticlassInto`; per-row `MulticlassProficiencies`; never blocks save). ✓
- *View mode lists all classes* → Task 4. ✓

**Placeholder scan:** every code step shows the exact markup/code. The one non-code judgement — the manual smoke (Task 4 Step 3) — is unavoidable given no bUnit and is explicitly scoped.

**Type consistency:** `MulticlassRules.KnownClasses` (`IReadOnlyList<string>`), `ClassLevel { Class; Level; Subclass }`, `CharacterSheet.Classes`/`.Level`, `CharacterSheet.ProficiencyBonusForLevel(int)`, `CanMulticlassInto(string, CharacterSheet) → PrereqResult(Allowed, Reason)`, `MulticlassProficiencies(string) → IReadOnlyList<string>`, `AddClass()`/`RemoveClass(int)` — used identically across tasks and matching the shipped `multiclass-character` signatures.
