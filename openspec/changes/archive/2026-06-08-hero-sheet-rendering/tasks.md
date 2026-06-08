## 1. Verify as-built

- [x] 1.1 Confirm `HeroDetail.razor` renders every spec'd section (identity, ability scores, combat, proficiencies & languages, features & traits, equipment)
- [x] 1.2 Confirm spellcasting section visibility matches the conditional scenario
- [x] 1.3 Confirm view/edit toggle persists edits and snapshot viewing leaves the current sheet unchanged
- [x] 1.4 Note any mismatch between spec and page; only fix mismatches (no new fields)

## 2. Verification

- [x] 2.1 `dotnet build` (warnings-as-errors) green
- [x] 2.2 Manual: open a populated hero, toggle edit/save, view a snapshot
