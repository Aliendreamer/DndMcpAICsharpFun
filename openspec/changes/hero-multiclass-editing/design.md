## Context

The archived `multiclass-character` change made `CharacterSheet.Classes: List<ClassLevel>` the source of truth (with derived `Class`/`Subclass`/`Level`/`ProficiencyBonus` getters, `ProficiencyBonusForLevel`, tolerant JSON migration) and shipped `MulticlassRules` (prerequisites + reduced proficiency subsets, all 13 classes). `HeroDetail.razor` was only minimally touched then — it still edits a single class via `_editClass`/`_editSubclass`/`_editLevel` locals and collapses to one class via `SetSingleClass` on save. This slice makes the editor multiclass-native. It is UI glue over already-tested domain logic; the only new domain surface is a class-name list.

## Goals / Non-Goals

**Goals:**
- Edit a per-class list (add/remove class/level/subclass rows) with the list as the source of truth; never collapse a multiclass hero on save.
- Live derived total level + proficiency bonus in the editor; view mode lists all classes.
- Non-blocking inline multiclass validity + reduced-proficiency advisory, computed on the in-progress sheet.

**Non-Goals:**
- bUnit / component-test infra; blocking/enforcing prereqs on save; duplicate-class enforcement; spell-slot preview; any change to persistence, resolution, or MCP tools beyond `KnownClasses`.

## Decisions

- **`MulticlassRules.KnownClasses` is the dropdown's single source.** A public `IReadOnlyList<string>` of the 13 names, kept identical to the prerequisite/proficiency map keys (unit-tested to match). *Alternative:* hardcode the list in the Razor — rejected (two sources drift; the rules engine keys on these exact names).
- **Rows bind directly to `_editSheet.Classes[idx]`; no intermediate locals.** Add appends a `new ClassLevel()`, Remove does `Classes.RemoveAt(idx)`, exactly like the existing Features-list editor. `ConfirmSaveAsync` therefore needs no class mapping — the list is already correct, so the `SetSingleClass` line is deleted. This removes the lossy mapping entirely rather than patching it, which is also why there is nothing new to unit-test in the save path.
- **Class name via `<select>` bound to `KnownClasses`; subclass free-text.** Guarantees `CanMulticlassInto`/`MulticlassProficiencies`/`SpellcastingAbility` lookups always hit a real key. Subclass stays free-text (open-ended; only specific subclass strings matter to the rules and those are the user's responsibility). New `ClassLevel` rows default their class to `KnownClasses[0]` so the `<select>` has a valid initial value.
- **Advisory is pure and in-memory.** `MulticlassRules.CanMulticlassInto(row.Class, _editSheet)` and `MulticlassProficiencies(row.Class)` run synchronously against `_editSheet` on each render — no DB, no MCP. The DB-backed `check_multiclass` tool stays for the chat agent (it resolves a *saved* snapshot; the editor works on unsaved state). Advisory is shown only for **non-primary** rows (index > 0) — the primary class has no multiclass-into prerequisite, so flagging it would be misleading. It is display-only and never blocks save.
- **Derived readouts recompute from `_editSheet`, not a stale copy.** Total level = `_editSheet.Level`; Prof. bonus = `CharacterSheet.ProficiencyBonusForLevel(_editSheet.Level)` (the same live-recompute pattern used for the existing PB readout). Because rows bind straight into `Classes`, Blazor re-renders these on every edit.
- **No bUnit.** The project has no Blazor component-test harness; adding one for this glue slice is out of scope (YAGNI). The new testable domain surface (`KnownClasses`) is unit-tested; the rules it feeds are already tested; the Razor is build-verified (warnings-as-errors) + manually checked.

## Risks / Trade-offs

- **[Razor markup has no automated test]** → Mitigation: keep all logic in already-tested domain methods; the component is thin binding + calls to tested code; warnings-as-errors catches type/binding breakage at build.
- **[`<select>` initial value mismatch]** → Mitigation: default a new `ClassLevel.Class` to `KnownClasses[0]` so the bound value is always a valid option; existing heroes' class strings already come from valid data.
- **[Empty Classes list]** → Mitigation: allowed and defined — a class-less sheet reads Level 0 / PB +2 and view mode shows "—"; no crash (derived getters already guard `Classes.Count == 0`).
- **[Advisory confuses users on homebrew]** → Mitigation: advisory is muted/non-blocking; save always proceeds.

## Migration Plan

No data migration. Existing single-class heroes already load as a one-entry `Classes` list (tolerant migration from the archived change) and render as one row. New multiclass heroes are authored via the editor. Rollback is reverting the Razor + removing `KnownClasses`; no data shape changes.

## Open Questions

- None blocking.
