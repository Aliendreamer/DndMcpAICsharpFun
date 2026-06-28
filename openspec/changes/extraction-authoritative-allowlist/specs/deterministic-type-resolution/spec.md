## MODIFIED Requirements

### Requirement: Deterministic per-candidate type resolution before the union

The system SHALL resolve each candidate through one deterministic ladder before falling through
to content-first union extraction. The ladder SHALL, in order: (1) when the candidate's name matches
the 5etools authoritative index, force the matched type and substitute the canonical name; (2)
otherwise, drop a candidate whose name is not entity-like (this precedes the stat-block check so a
tutorial/fragment heading is never forced `Monster`); (3) otherwise, force `Monster` when the candidate
has a complete stat block (the stat-block rescue guard, which fires even for official books so a real
monster is never dropped); (4) otherwise, force `MagicItem` when the candidate has a magic-item
signature; (5) otherwise, for an official book (the book has a `FivetoolsSourceKey`) when every one of
the candidate's prior types is in the gated set (Spell, Monster, Class, Race, Background, Feat,
Condition, God), the candidate's PRIMARY prior type (the first, bookmark-derived entry of its prior
list) is in the gated set, **decline** the candidate with reason `no_5etools_match` and make no
extraction LLM call; (6) otherwise defer to the content-first union (pick-or-decline), unchanged.
(The gate keys off the PRIMARY prior because the scanner always appends a frequency floor — including
the ungated Item — to every candidate's prior list, so an "all priors gated" test would never fire.)

#### Scenario: 5etools match forces the matched type and canonical name
- **WHEN** a candidate name matches the 5etools index (e.g. "FIREBALL" → Spell, canonical "Fireball")
- **THEN** it is forced to the matched type with the canonical name, skipping the LLM type decision

#### Scenario: Complete stat block with a creature-like name forces Monster
- **WHEN** a candidate has a complete stat block and a creature-like name (e.g. "Aboleth") and does not match the index
- **THEN** it is extracted as `Monster` directly, even for an official book, without being declined

#### Scenario: Magic-item signature forces MagicItem
- **WHEN** a candidate has a magic-item signature (e.g. Vorpal Sword: "legendary", "requires attunement") and does not match the index
- **THEN** it is extracted as `MagicItem`, not `Item` or a declined branch

#### Scenario: Official gated-type non-match is declined before extraction
- **WHEN** an official book yields a candidate whose primary prior type is gated (e.g. "Rage" with primary prior {Class}, even though the scanner also appended the {Monster, Spell, Item, Class} floor), with no 5etools match and no complete stat block
- **THEN** the candidate is declined with reason `no_5etools_match` and no extraction LLM call is made for it

#### Scenario: Non-entity-named candidate is dropped before extraction
- **WHEN** a candidate's name is not entity-like (e.g. "ACTIONS") and it does not match the index
- **THEN** the candidate is dropped (not declined, no record) and no extraction LLM call is made for it

#### Scenario: Override guard — tutorial/fragment stat block is not forced Monster
- **WHEN** a candidate has stat-block-like text but a non-entity-like name (e.g. "Step 2. Basic Statistics", "Challenge 7 (2,900 XP)") and does not match the index
- **THEN** the Monster override does NOT fire (the candidate is dropped at the entity-like step before the stat-block check)

#### Scenario: Ordinary candidate still uses content-first union
- **WHEN** a candidate is entity-like, has neither a complete stat block nor a magic-item signature, does not match the index, and is not an official gated-primary non-match (e.g. a homebrew candidate, or an official candidate whose primary prior type is ungated such as {Item})
- **THEN** it is extracted via the content-first union pick-or-decline exactly as before
