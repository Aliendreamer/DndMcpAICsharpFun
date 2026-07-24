# Tasks — structured-armor-and-ac (WornArmor field + ArmorCatalog + armor class resolver + hero-editor UI)

## 1. `WornArmor` field on `CharacterSheet` (JSON, no migration)
- [ ] 1.1 Add `sealed class WornArmor { public string ArmorName {get;set;} = ""; public bool Shield {get;set;} public int MagicBonus {get;set;} }` and a `public WornArmor WornArmor { get; set; } = new();` property on `CharacterSheet`. It serializes into the existing JSON column — NO EF migration.
- [ ] 1.2 Prove no migration + old-snapshot tolerance: a unit test deserializing a legacy `CharacterSheet` JSON with NO `WornArmor` key → `WornArmor` is non-null unarmored default. (Optional guard: `dotnet ef migrations add VerifyNeutral` → Up/Down empty → delete + `git checkout` the snapshot; only if unsure the JSON column truly needs no migration.)

## 2. `ArmorCatalog` (static)
- [ ] 2.1 Create `Features/Resolution/ArmorCatalog.cs`: `enum ArmorCategory { Light, Medium, Heavy }` + a static case-insensitive `name → (int BaseAc, ArmorCategory Category)` map for the PHB set (Padded 11/L, Leather 11/L, Studded Leather 12/L, Hide 12/M, Chain Shirt 13/M, Scale Mail 14/M, Breastplate 14/M, Half Plate 15/M, Ring Mail 14/H, Chain Mail 16/H, Splint 17/H, Plate 18/H); `Lookup(name) : (int,ArmorCategory)?` null for unknown; and an ordered `Names` list for the UI dropdown.
- [ ] 2.2 Unit tests: `Lookup("Chain Mail")` → (16, Heavy); `Lookup("chain mail")` (case-insensitive) → same; `Lookup("Mithral Plate")` (not in set) → null.

## 3. `armor class` resolver (pure)
- [ ] 3.1 Add a static Unarmored-Defense helper (mirror `SavingThrowProficiencies`): Barbarian → `10 + Dex + Con` (shield allowed); Monk → `10 + Dex + Wis` (only if no shield). Compute the applicable options across `sheet.Classes`.
- [ ] 3.2 Add an `armor class` branch to `ResolveForSheetAsync` + a PURE static `ResolveArmorClass(CharacterSheet sheet)` (mirror `ResolveSavingThrows`, `Task.FromResult` dispatch). Unarmored `10 + Dex` raised to the best applicable UD; Light `base + Dex`; Medium `base + min(Dex,2)`; Heavy `base`; `+2` shield; `+ MagicBonus`. Components: armor/unarmored-defense, dex, shield, magic; total in Value; null provenance. Unknown armor name → `needsReview`.
- [ ] 3.3 Unit tests — ONE PER BRANCH: Heavy ignores Dex (Plate+Dex16 → 18); Medium caps Dex at 2 (Half Plate+Dex18 → 17); Light full Dex (Leather+Dex16 → 14); shield+magic add (Chain Mail+shield+1 → 19); Barbarian UD with shield (unarmored Dex14/Con16+shield → 17); Monk UD suppressed by shield (falls back to 10+Dex+2); multiclass takes higher UD; unknown armor → `needsReview`; default/empty `WornArmor` → unarmored (no throw). RED-first.

## 4. Hero-editor UI (capture only)
- [ ] 4.1 In `HeroDetail.razor` edit form (near the AC input ~line 260): ensure `_editSheet.WornArmor` is non-null on load; add a `<select>` bound to `WornArmor.ArmorName` (options `None` + `ArmorCatalog.Names`), a shield `<input type="checkbox">` bound to `WornArmor.Shield`, and a magic-bonus `<input type="number">` bound to `WornArmor.MagicBonus`. No AC calc in the UI. Reuse existing form/label classes.
- [ ] 4.2 Presentational gate: build 0/0 + FULL suite stays green (behavior unchanged); grep every class the new markup references against `wwwroot/app.css`; rebuild the app container (`docker compose up -d --build app`, re-login test/test) and Playwright-screenshot the armor controls at desktop (~1280) AND mobile (~390) + a `browser_evaluate` horizontal-overflow check.

## 5. Gates
- [ ] 5.1 `dotnet build` 0/0; FULL `dotnet test` green; `dotnet format` clean on touched files; `git diff --stat` confined to `Domain/CharacterSheet.cs` + `Features/Resolution/*` + `CompanionUI/Pages/Campaigns/HeroDetail.razor` + tests; NO migration file added; no `.http`/insomnia change.
