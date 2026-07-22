# Cross-Type Recovery — Minimal Item-Rescue Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan. Steps use checkbox (`- [ ]`) syntax.

**Goal:** A decline-bound candidate with a SPECIFIC mundane weapon/armor stat signature is admitted as `Item`, checked BEFORE the Rule rescue (so a genuine item is never mis-typed `Rule`, and a rule is never grabbed as `Item`). Defensive/dormant-but-safe on the current corpus.

**Architecture:** Exact mirror of the shipped Rule rescue (extraction-content-classification #2). Add `ExtractionSignatures.ItemSignature` (a stat-marker, not a keyword) + `EntityExtractionOrchestrator.RescueAsItemOrNull`, wired BEFORE `RescueAsRuleOrNull` at the two decline points. A `TypePrior=[Item]` swap → union offers Item-or-none → an Item pick is `Accepted`/`canon-unindexed` (Item is non-gated). No schema/prompt/renderer change (Item is fully wired already).

**Tech Stack:** C# / .NET 10, xunit + FluentAssertions. Serena for all `.cs`.

## Global Constraints
- **Serena only** for `.cs`; grep-verify after each edit. Built-in Read/Edit on `.cs` forbidden.
- **Work on `main`**; commit each task after the reviewer passes.
- Warnings-as-errors → `dotnet build` 0/0. `dotnet` needs `dangerouslyDisableSandbox: true`.
- Ignore LSP false CS0246/CS1061 on test files; trust `dotnet build`/test.
- No HTTP endpoint change. `GatedTypes` unchanged (Item already non-gated). No schema/prompt/renderer change.
- **STOP before the live re-extract** — validated jointly with #2 by the DMG re-extract (next step).

**Existing (shipped in #2, verbatim):**
```csharp
// EntityExtractionOrchestrator (internal):
private static EntityCandidate? RescueAsRuleOrNull(EntityCandidate candidate, DeterministicOutcome outcome) =>
    outcome == DeterministicOutcome.Decline && ExtractionSignatures.RuleSignature(candidate)
        ? candidate with { TypePrior = new[] { EntityType.Rule } } : null;
// Used at RunFullExtractionAsync decline block and RunErrorsOnlyAsync silent-skip; item rescue goes BEFORE it.
// ExtractionSignatures already has: HasArmorClass(text), IsMagicItem(text), RuleSignature(candidate).
```

---

## Task 1: `ItemSignature` + `RescueAsItemOrNull` (before Rule rescue)

**Files:**
- Modify: `Features/Ingestion/EntityExtraction/ExtractionSignatures.cs`
- Modify: `Features/Ingestion/EntityExtraction/EntityExtractionOrchestrator.cs`
- Test: `DndMcpAICsharpFun.Tests/Entities/Extraction/ItemRescueTests.cs`

**Interfaces:**
- `ExtractionSignatures.ItemSignature(EntityCandidate candidate) : bool` — a weapon damage-type token OR an armor AC+cost line.
- `EntityExtractionOrchestrator.RescueAsItemOrNull(EntityCandidate, DeterministicOutcome) : EntityCandidate?` — `TypePrior=[Item]` when `Decline && ItemSignature`, else null. Called BEFORE `RescueAsRuleOrNull`.

- [ ] **Step 1: Write the failing tests** (read the existing `RuleRescueTests.cs` via Serena to mirror `EntityCandidate` construction + the `internal` helper access via InternalsVisibleTo):

```csharp
// ItemSignature
[Fact] public void ItemSignature_true_for_weapon_damage_line()
    => ExtractionSignatures.ItemSignature(new EntityCandidate(EntityType.Monster, "Longsword",
        "A martial melee weapon. Cost 15 gp. 1d8 slashing damage, versatile (1d10). Weight 3 lb.", 149)).Should().BeTrue();
[Fact] public void ItemSignature_true_for_armor_stat_line()
    => ExtractionSignatures.ItemSignature(new EntityCandidate(EntityType.Monster, "Chain Mail",
        "Heavy armor. Armor Class 16. Cost 75 gp. Weight 55 lb. Strength 13 required.", 145)).Should().BeTrue();
[Fact] public void ItemSignature_false_for_a_rule()
    => ExtractionSignatures.ItemSignature(new EntityCandidate(EntityType.Class, "Switching Weapons",
        new string('x', 60) + " When you attack, you can switch the weapon you are holding as part of the same action, without using your object interaction.", 195)).Should().BeFalse();
[Fact] public void ItemSignature_false_for_short_fragment()
    => ExtractionSignatures.ItemSignature(new EntityCandidate(EntityType.Item, "x", "too short", 1)).Should().BeFalse();
```
Plus the ordering test (item-before-rule) via the two internal helpers:
```csharp
[Fact] public void Item_rescue_takes_priority_over_rule_for_item_signature_candidate()
{
    var weapon = new EntityCandidate(EntityType.Monster, "Longsword",
        new string('x', 220) + " 1d8 slashing damage. Cost 15 gp.", 149); // both item-sig AND >=200 prose
    var item = EntityExtractionOrchestrator.RescueAsItemOrNull(weapon, DeterministicOutcome.Decline);
    item!.TypePrior.Should().BeEquivalentTo(new[] { EntityType.Item });
    // and the rule rescue would NOT run because item rescue returns non-null first (covered by the orchestrator wiring)
}
[Fact] public void Item_rescue_null_for_a_rule_which_falls_through_to_rule_rescue()
{
    var rule = new EntityCandidate(EntityType.Class, "Switching Weapons",
        new string('x', 220) + " you can switch the weapon you are holding.", 195);
    EntityExtractionOrchestrator.RescueAsItemOrNull(rule, DeterministicOutcome.Decline).Should().BeNull();
    EntityExtractionOrchestrator.RescueAsRuleOrNull(rule, DeterministicOutcome.Decline).Should().NotBeNull(); // it IS rule-eligible
}
```
(If `RescueAsRuleOrNull` is `internal static`, the test project already has InternalsVisibleTo — confirm; make `RescueAsItemOrNull` the same `internal static`.)

- [ ] **Step 2: Run → FAIL** (`ItemSignature`/`RescueAsItemOrNull` missing). `dotnet test --filter FullyQualifiedName~ItemRescue` (dangerouslyDisableSandbox).

- [ ] **Step 3: Add `ItemSignature`** — Serena `insert_after_symbol` near `RuleSignature` in `ExtractionSignatures.cs`:

```csharp
    /// <summary>
    /// A SPECIFIC mundane-item stat signature: a weapon damage-type token or an armor AC+cost stat
    /// line — markers a rules passage lacks. Used to rescue a decline-bound candidate as an Item
    /// BEFORE the Rule rescue, so a genuine item is never mis-typed Rule and a rule is never grabbed
    /// as Item (extraction-cross-type-recovery). Deliberately narrow.
    /// </summary>
    public static bool ItemSignature(EntityCandidate candidate)
    {
        var t = candidate.Text;
        if (string.IsNullOrWhiteSpace(t)) return false;
        // weapon: "1d8 slashing" / "2d6 piercing" / "d4 bludgeoning"
        if (WeaponDamage().IsMatch(t)) return true;
        // armor: an AC figure paired with a gp/sp cost
        if (HasArmorClass(t) && Cost().IsMatch(t)) return true;
        return false;
    }

    [GeneratedRegex(@"\d*d\d+\s+(slashing|piercing|bludgeoning)", RegexOptions.IgnoreCase)]
    private static partial Regex WeaponDamage();

    [GeneratedRegex(@"\b\d+\s?(gp|sp)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Cost();
```
(If `ExtractionSignatures` is not already `partial` with `[GeneratedRegex]`, either make the class `public static partial class` — check the file first — or use compiled `Regex` static fields instead. Match the file's existing style; grep for `GeneratedRegex`/`partial` in it first.)

- [ ] **Step 4: Add `RescueAsItemOrNull` + wire before the Rule rescue** — Serena edits in `EntityExtractionOrchestrator.cs`. Add the helper:
```csharp
    // Rescue a would-be-declined candidate that carries a mundane weapon/armor stat signature as an
    // Item — checked BEFORE the Rule rescue so a genuine item is never mis-typed Rule. TypePrior is
    // replaced with [Item] only (the failed gated type is not re-offered).
    internal static EntityCandidate? RescueAsItemOrNull(EntityCandidate candidate, DeterministicOutcome outcome) =>
        outcome == DeterministicOutcome.Decline && ExtractionSignatures.ItemSignature(candidate)
            ? candidate with { TypePrior = new[] { EntityType.Item } }
            : null;
```
At BOTH decline points, change the rescue to try Item first, then Rule. In `RunFullExtractionAsync`:
```csharp
        if (declineRes.Outcome == DeterministicOutcome.Decline)
        {
            var rescued = RescueAsItemOrNull(candidate, declineRes.Outcome)
                       ?? RescueAsRuleOrNull(candidate, declineRes.Outcome);
            if (rescued is null)
            {
                // …unchanged decline: declined.Add(...); continue;
            }
            candidate = rescued;
        }
```
Mirror in `RunErrorsOnlyAsync`:
```csharp
        if (errDecline.Outcome == DeterministicOutcome.Decline)
        {
            var rescued = RescueAsItemOrNull(candidate, errDecline.Outcome)
                       ?? RescueAsRuleOrNull(candidate, errDecline.Outcome);
            if (rescued is null) continue;
            candidate = rescued;
        }
```
(Read the actual current decline blocks via Serena — they now contain the `?? RescueAsRuleOrNull` from #2 in a single-rescue form; replace that single call with the `RescueAsItemOrNull(...) ?? RescueAsRuleOrNull(...)` chain. Grep-verify both landed and the `candidate` reassignment reaches the downstream `runner.ExtractOneAsync`.)

- [ ] **Step 5: Run tests → pass; whole-solution `dotnet build` → 0/0.** Format the touched files.
- [ ] **Step 6: Commit:** `feat(extraction): item-rescue decline-bound weapon/armor-stat candidates as Item (before Rule)`

---

## Task 2: Deterministic harness (moot-but-safe evidence)

**Files:**
- Test: extend `DndMcpAICsharpFun.Tests/Entities/Extraction/RuleRescueHarnessTests.cs` (or a new `ItemRescueHarnessTests.cs`).

- [ ] **Step 1:** On the real DMG candidate list (reuse the harness's loader), compute each decline-bound candidate's `ItemSignature`. Assert: the item-rescue count is SMALL (log it — expected ~0, since the corpus has no real mundane-item declines) and that the specific `switching-weapons` decline (a rule) has `ItemSignature == false` (item and rule rescues are disjoint on the rules). Log item-rescued vs rule-rescued counts. This documents dormant-but-safe (no false item grabs on real declines).
- [ ] **Step 2:** `dotnet build` 0/0; harness passes; capture the counts. Commit: `test(extraction): harness confirms item-rescue is dormant-but-safe on real declines`.

---

## Task 3: Gates + STOP
- [ ] **Step 1:** Whole-solution build 0/0; FULL `dotnet test` green (note count); `dotnet format --include <touched files>` clean; `git diff --stat` = only `ExtractionSignatures.cs`, `EntityExtractionOrchestrator.cs`, the test file(s). `.http`/insomnia untouched.
- [ ] **Step 2: STOP.** Do NOT run a separate live re-extract — this is validated JOINTLY with #2 by the DMG live re-extract (the next step). Report green + the harness counts.

---

## Self-Review notes
- Spec "mundane-item signature → Item, before Rule" → Task 1 (ItemSignature + item-before-rule wiring + ordering test). "a rule is never item-rescued" → the `switching-weapons` false test + harness. "real entity never rescued" → the decline-only gate (unchanged from #2).
- Item is non-gated (no `GatedTypes` change); TypePrior REPLACED with `[Item]`; `candidate.Type` untouched. No schema/prompt/renderer change (Item fully wired).
- Live re-extract deferred to the joint #2+#3 DMG run.
