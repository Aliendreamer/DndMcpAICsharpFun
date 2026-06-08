# Hero Sheet Rendering — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Verify the as-built `HeroDetail.razor` character-sheet rendering matches the `hero-sheet-rendering` spec; fix only genuine mismatches.

**Architecture:** Verification only. Code inspection confirmed the page already renders every `CharacterSheet` field across seven sections (identity, ability scores, combat, conditional spellcasting, proficiencies & languages, features & traits, equipment) with a view/edit toggle and snapshot viewing. No new behavior is planned.

**Tech Stack:** Blazor Server; `Domain/CharacterSheet.cs`.

> **Reality note:** This change documents existing behavior. Expect **zero code changes**. If verification finds a divergence between the page and a spec scenario, fix the page (not the spec) to match.

---

## File Structure

- `Components/Pages/Campaigns/HeroDetail.razor` — verify against spec
- `Domain/CharacterSheet.cs` — read-only reference

---

### Task 1: Verify sections render

- [ ] **Step 1: Map each spec requirement to the page**

Open `Components/Pages/Campaigns/HeroDetail.razor`. Confirm sections exist for: identity, ability scores, combat (HP/AC/speed/initiative/prof bonus), proficiencies & languages (armor/weapons/tools/languages/skills), features & traits, equipment. Confirm populated list fields render and empty optional groups are conditionally hidden.

- [ ] **Step 2: Verify the conditional spellcasting scenario**

Confirm the spellcasting section renders only when `sheet.SpellcastingAbility` is non-empty (or in edit mode), showing save DC, attack bonus, per-level slots, and known spells — matching the spec's two-part scenario.

---

### Task 2: Verify edit + snapshot behavior

- [ ] **Step 1: View/edit toggle**

Confirm edit mode binds fields to `_editSheet` and saving persists and returns to view mode showing new values.

- [ ] **Step 2: Snapshot viewing**

Confirm selecting a prior snapshot renders that snapshot's sheet (`_viewingSnapshot`) without mutating the current sheet.

- [ ] **Step 3: Record findings**

If everything matches, note "verified, no change." If a scenario diverges, make the minimal page fix and note it.

---

### Task 3: Verification

- [ ] **Step 1: Build + test**

Run: `dotnet build && dotnet test`
Expected: green, zero warnings.

- [ ] **Step 2: Manual smoke**

Open a populated hero → all sections render; toggle edit, change a field, save → persists; view a snapshot → current sheet unchanged.

- [ ] **Step 3: Commit (only if a mismatch was fixed)**

```bash
git add -A && git commit -m "fix(hero-sheet): align rendering with spec"
```
