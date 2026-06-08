## Why

The hero detail page already renders a complete D&D character sheet (identity, ability scores, combat, spellcasting, proficiencies, languages, features, equipment) with view/edit modes and snapshot history, but this behavior has no spec. Capturing it as-built locks in the contract and guards against regressions.

## What Changes

- Document the existing `CharacterSheet` rendering on `HeroDetail.razor` as requirements: the section structure, view/edit toggle, and snapshot viewing.
- No new behavior — implementation is verification that the page matches the spec.

## Capabilities

### New Capabilities
- `hero-sheet-rendering`: how a hero's `CharacterSheet` is displayed and edited on the hero detail page.

### Modified Capabilities
<!-- none -->

## Impact

- `Components/Pages/Campaigns/HeroDetail.razor` (verified against spec; no behavior change expected)
- `Domain/CharacterSheet.cs` (read-only reference for fields rendered)
