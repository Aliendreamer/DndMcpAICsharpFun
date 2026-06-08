## Context

`HeroDetail.razor` renders a `CharacterSheet` (from `_hero.LatestSnapshot?.Sheet` or a viewed snapshot) across seven sections: Identity, Ability Scores, Combat, Spellcasting (conditional on a spellcasting ability), Proficiencies & Languages, Features & Traits, and Equipment. It supports a view/edit toggle that binds to an `_editSheet`, and snapshot viewing via `_viewingSnapshot`. This is fully implemented; the change exists to document it.

## Goals / Non-Goals

**Goals:**
- Capture the as-built rendering and edit/snapshot behavior as a verifiable spec.

**Non-Goals:**
- Any visual or behavioral change to the sheet.
- New `CharacterSheet` fields.

## Decisions

- **Document as-built, no enhancement.** Inspection showed the sheet already renders every `CharacterSheet` field; there is no meaningful gap to add. Implementation is a verification pass, not new code. This was confirmed with the user before writing the spec.

## Risks / Trade-offs

- [Spec lags future field additions] → Spec describes section structure and the "all populated fields are shown" contract rather than enumerating every field, so it tolerates additive growth.
