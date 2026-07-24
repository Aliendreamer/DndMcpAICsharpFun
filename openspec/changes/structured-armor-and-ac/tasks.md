# Tasks — structured-armor-and-ac (WornArmor field + ArmorCatalog + armor class resolver + hero-editor UI)

All tasks complete. Commits 897b928, 04e98c1, 67e3d5a, 8337316 (+ cosmetic fixes 14636c0). Full suite 1694/1694; whole-branch review APPROVE; UI visually verified live (desktop + mobile).

## 1. `WornArmor` field on `CharacterSheet` (JSON, no migration)
- [x] 1.1 `WornArmor { ArmorName, Shield, MagicBonus }` type + `WornArmor` property on `CharacterSheet`, JSON column, NO migration. (897b928)
- [x] 1.2 Old-snapshot tolerance test: legacy JSON with no `WornArmor` key → non-null unarmored default; `git status` confirmed no `Migrations/` file. (897b928)

## 2. `ArmorCatalog` (static)
- [x] 2.1 `Features/Resolution/ArmorCatalog.cs`: `ArmorCategory` enum + 12-entry PHB map + `Lookup` (case-insensitive) + ordered `Names`. (04e98c1)
- [x] 2.2 Unit tests: Lookup base-AC+category, case-insensitive, unknown → null, Names count. (04e98c1)

## 3. `armor class` resolver (pure)
- [x] 3.1 `UnarmoredDefense` helper (Barbarian 10+Dex+Con shield-ok; Monk 10+Dex+Wis no-shield; multiclass higher). (67e3d5a)
- [x] 3.2 `armor class` branch + pure `ResolveArmorClass`: unarmored→best UD, Light/Medium(cap 2)/Heavy Dex rules, +2 shield, +magic, null provenance, unknown → needsReview. (67e3d5a; UD-breakdown polish 14636c0)
- [x] 3.3 9 per-branch unit tests + 1 UD-breakdown regression test (10 total). (67e3d5a, 14636c0)

## 4. Hero-editor UI (capture only)
- [x] 4.1 Armor dropdown (None + `ArmorCatalog.Names`) + shield checkbox + magic-bonus in the Combat edit-grid; `_editSheet.WornArmor ??= new()` on edit-begin; `@using …Features.Resolution`. (8337316; shield `.checkbox-label` inline fix 14636c0)
- [x] 4.2 Presentational gate: build 0/0, full suite green, classes resolve (edit-grid), live screenshots desktop 1280 + mobile 390, no horizontal overflow. Shield-inline fix re-verified live. (controller)

## 5. Gates
- [x] 5.1 build 0/0; FULL `dotnet test` 1694/1694; `dotnet format` clean; diff confined to Domain/CharacterSheet.cs + Features/Resolution/* + HeroDetail.razor + wwwroot/app.css + tests; NO migration file; no `.http`/insomnia change. Whole-branch review APPROVE.
