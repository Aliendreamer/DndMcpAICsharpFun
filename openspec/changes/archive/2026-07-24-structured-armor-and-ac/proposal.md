## Why

`CharacterResolutionService` now resolves class resources, saving throws, and spell counts (Spec A, `resolution-features`), but Armor Class was deferred: the character's AC lives only as a manually-typed `CharacterSheet.ArmorClass` int, and `Equipment` is free-text — so AC cannot be derived or grounded. This is Spec B, the deferred half of that split. Adding a small structured worn-armor field (which needs **no migration** — `CharacterSheet` is a JSON column, so a new property just deserializes to its default on old snapshots) lets a new pure `armor class` resolver compute AC deterministically from the armor + Dexterity + shield + magic + class Unarmored Defense, consistent with the other resolver features (cited/`needsReview`, never fabricated).

## What Changes

- Add a structured `WornArmor` value to `CharacterSheet` (`{ string ArmorName; bool Shield; int MagicBonus; }`, JSON-serialized). `ArmorName` `""`/`"None"` means unarmored. The existing manual `ArmorClass` int is untouched (a separate value the resolver does not read or write).
- Add a static `ArmorCatalog` (`name → (baseAc, category Light/Medium/Heavy)`) for the PHB armor set — same static-data pattern as `SavingThrowProficiencies`. An `ArmorName` not in the catalog (and not unarmored) → the resolver returns `needsReview` (no fabricated base AC). Custom/homebrew armor is out of scope (a later change).
- Add an `armor class` feature to `CharacterResolutionService` — a **pure** resolver (no DB): unarmored `10 + Dex` then the best applicable Unarmored Defense (Barbarian `10 + Dex + Con`, shield allowed; Monk `10 + Dex + Wis`, only with no shield); Light `base + Dex`; Medium `base + min(Dex, 2)`; Heavy `base`; `+2` shield; `+ MagicBonus`. Returns a `ResolvedFact` (components: armor base, Dex, shield, unarmored-defense, magic; total), computed → null provenance; unknown armor → `needsReview`.
- Add hero-editor UI (`HeroDetail.razor`) near the AC input: an armor dropdown (None + catalog names) bound to `WornArmor.ArmorName`, a shield checkbox, and a magic-bonus number. No live AC calc in the UI — it only captures the structured data; the resolver owns AC.

## Capabilities

### New Capabilities

- `structured-armor-and-ac`: a character carries a structured worn-armor selection (armor name + shield + magic bonus), and `CharacterResolutionService` resolves an `armor class` fact deterministically from it — armor base AC by category with the correct Dex rule, shield, magic bonus, and class Unarmored Defense (Barbarian/Monk) — cited as computed and `needsReview` (never fabricated) when the armor is unknown.

### Modified Capabilities

<!-- Additive: a new field, a new resolver feature, a new UI control. No existing spec's REQUIREMENTS change. -->

## Impact

- `Domain/CharacterSheet.cs` — new `WornArmor` property + a `WornArmor` type (JSON column, no migration).
- `Features/Resolution/ArmorCatalog.cs` (new, static) + `Features/Resolution/CharacterResolutionService.cs` (`armor class` branch + pure `ResolveArmorClass`).
- `CompanionUI/Pages/Campaigns/HeroDetail.razor` — armor dropdown + shield checkbox + magic-bonus input in the edit form.
- Reuses `CharacterSheet.Modifier`, `sheet.Class`/`Classes`, the `ResolvedFact`/`ResolvedComponent` shape, and the static-map pattern from `SavingThrowProficiencies`.
- Tests: per-branch unit tests for the AC resolver; behavior-unchanged full suite green; a UI screenshot of the armor controls (presentational-change gate).
- **No EF migration** (JSON column). No HTTP endpoint change; no `.http`/insomnia change.
