## 1. Domain — KnownClasses source

- [ ] 1.1 (test) `MulticlassRules.KnownClasses` has exactly the 13 class names and each resolves in `CanMulticlassInto` (allowed or a real reason, not "Unknown class") and returns from `MulticlassProficiencies`
- [ ] 1.2 Add `public static readonly IReadOnlyList<string> KnownClasses` to `MulticlassRules`, ordered, matching the prerequisite/proficiency map keys; test green; build 0/0

## 2. Edit form — per-class list editor

- [ ] 2.1 Replace the three single-class inputs (Class/Subclass/Level) in `HeroDetail.razor`'s edit grid with a repeatable class-row editor over `_editSheet.Classes` (mirror the Features-list `@for` pattern): per row a class `<select>` bound to `KnownClasses`, a number input for level, a free-text subclass, a Remove button; an "+ Add class" button appends a `new ClassLevel { Class = MulticlassRules.KnownClasses[0] }`
- [ ] 2.2 Remove the `_editClass`/`_editSubclass`/`_editLevel` fields and their initialization in `EnterEdit`; remove the `SetSingleClass(...)` call in `ConfirmSaveAsync` (the `Classes` list is already the edited source of truth)
- [ ] 2.3 Add read-only derived readouts in the edit grid: Total level = `@_editSheet.Level` and Prof. bonus = `+@CharacterSheet.ProficiencyBonusForLevel(_editSheet.Level)` (live, recomputes as rows change)
- [ ] 2.4 Empty-list safety: removing all rows leaves `Classes` empty (class-less sheet, Level 0) — no crash; build 0/0

## 3. Validity + proficiency advisory (non-blocking)

- [ ] 3.1 For each non-primary row (index > 0), render `MulticlassRules.CanMulticlassInto(row.Class, _editSheet)` as ✓ allowed or ⚠ "not allowed: {reason}" (muted, advisory), computed live on `_editSheet`
- [ ] 3.2 For each row, render the reduced subset from `MulticlassRules.MulticlassProficiencies(row.Class)` as muted text
- [ ] 3.3 Confirm the advisory never blocks save (Save button always enabled; no guard on prereq state)

## 4. View mode

- [ ] 4.1 Replace the single-class view line with a list of all classes ("{Class} {Level} ({Subclass})" joined) plus the existing total-level line

## 5. Validation

- [ ] 5.1 `dotnet build` 0/0 (warnings-as-errors); full non-persistence suite green (incl. the new `KnownClasses` test)
- [ ] 5.2 Manual UI smoke (build-run): open a single-class hero (renders one row), add a class (two rows, total level + PB update, advisory shows for the 2nd row), remove it, save — verify no class collapse; a genuine two-class hero round-trips through edit+save with both classes intact
- [ ] 5.3 Docs: no HTTP route change → `.http`/`.insomnia` untouched; note if any user-facing doc references the hero editor
