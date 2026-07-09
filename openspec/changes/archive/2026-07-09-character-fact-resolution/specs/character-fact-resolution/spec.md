## ADDED Requirements

### Requirement: Resolved-choices slot on the character sheet

`CharacterSheet` SHALL store resolved player choices as structured references in a `ResolvedChoices`
slot (e.g. `ancestry → "phb14.choiceset.draconic-ancestry:Red"`), separate from the flat fields and
the freeform `Features` list. Existing `HeroSnapshot` JSON SHALL remain readable (the field is
optional/empty when absent). `Level` remains an `int` in this slice.

#### Scenario: A character records its ancestry choice
- **WHEN** a Dragonborn hero selects the Red ancestry
- **THEN** `CharacterSheet.ResolvedChoices["ancestry"]` is `"phb14.choiceset.draconic-ancestry:Red"`

#### Scenario: Old snapshots still load
- **WHEN** a `HeroSnapshot` saved before this change is loaded
- **THEN** it deserializes successfully with `ResolvedChoices` empty (no migration error)

### Requirement: Deterministic character-fact resolution

A `CharacterResolutionService.resolve(heroId, feature)` SHALL compute a character-specific fact
deterministically from structured facts + character state, returning `{ value, components[],
provenance[], confidence }`. For `feature = "breath weapon"` it SHALL compose: the resolved
ancestry's table row (damage type, breath area, save ability) + the character's level → breath
damage dice (tier map: L1–5 = 1d10, L6–10 = 2d6, L11–15 = 3d6, L16–20 = 4d6) + the save DC
(`8 + proficiency bonus + Constitution modifier`). Each component SHALL carry its own provenance.
The chat LLM SHALL NOT compute these rule values; it only orchestrates the call and renders the
result.

#### Scenario: Red Dragonborn breath weapon at level 11
- **WHEN** `resolve(hero, "breath weapon")` is called for a Red Dragonborn, level 11, Con modifier +3
- **THEN** the result is fire damage, 15-ft cone, Dex save, DC 15 (8 + 4 + 3), 3d6, with provenance for each component

#### Scenario: Damage scales by tier
- **WHEN** the same character is resolved at level 3 vs level 17
- **THEN** the damage dice are 1d10 and 4d6 respectively (tier map), the rest unchanged

#### Scenario: needsReview fact falls back to prose
- **WHEN** a resolved component is `needsReview` (ungrounded)
- **THEN** the service returns that component's prose span instead of a computed value, with its provenance

### Requirement: MCP tool exposes resolution to chat

The MCP server SHALL expose `resolve_character_feature(heroId, feature)` returning the resolution
result, so the chat client can request a computed, cited character fact.

#### Scenario: Chat renders a cited answer
- **WHEN** the chat client calls `resolve_character_feature(hero, "breath weapon")` for the Red Dragonborn
- **THEN** it receives the structured result and the LLM can render "15-ft cone of fire, Dex save DC 15, 3d6 [PHB p.34]"
