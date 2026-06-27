# deterministic-type-resolution Specification

## Purpose
TBD - created by archiving change consolidate-extraction-signatures. Update Purpose after archive.
## Requirements
### Requirement: Single source of truth for content and name signatures

The system SHALL provide one utility (`ExtractionSignatures`) that answers content and name
recognition questions, and all stat-block / magic-item / name-quality checks in the extraction
pipeline SHALL route through it rather than re-matching marker strings independently.

#### Scenario: Complete stat block recognised
- **WHEN** a candidate's text contains "Armor Class", "Hit Points", and "Challenge"
- **THEN** `ExtractionSignatures.IsCompleteStatBlock` returns true

#### Scenario: Magic item recognised
- **WHEN** a candidate's text carries a magic-item signature (a rarity term such as "uncommon"/"rare"/"legendary", or "requires attunement", or "wondrous item")
- **THEN** `ExtractionSignatures.IsMagicItem` returns true

#### Scenario: Non-creature text is not a stat block
- **WHEN** a candidate's text mentions "Armor Class" and "Hit Points" but not "Challenge" (e.g. a vehicle/object)
- **THEN** `ExtractionSignatures.IsCompleteStatBlock` returns false

#### Scenario: Consolidation is the only matcher
- **WHEN** the pipeline needs to know whether text has a stat block (in the scanner, the deduplicator, or the resolver)
- **THEN** it calls `ExtractionSignatures`, and no other production type in the extraction feature matches the literal "Armor Class"/"Hit Points"/"Challenge" strings itself

### Requirement: Entity-like name quality check

The system SHALL classify a candidate name as entity-like or not, where headings and stat-block
fragments are NOT entity-like.

#### Scenario: Real entity name is entity-like
- **WHEN** the name is a normal entity name (e.g. "Aboleth", "Bag of Holding", "Fireball")
- **THEN** `ExtractionSignatures.IsEntityLikeName` returns true

#### Scenario: Heading or fragment name is not entity-like
- **WHEN** the name is a section heading or stat-block fragment (e.g. "ACTIONS", "Appendix D: Creature Statistics", "Step 2. Basic Statistics", "Challenge 7 (2,900 XP)")
- **THEN** `ExtractionSignatures.IsEntityLikeName` returns false

### Requirement: Deterministic per-candidate type resolution before the union

The system SHALL resolve each candidate through one deterministic ladder before falling through
to content-first union extraction. The ladder SHALL, in order: (1) drop a candidate whose name
is not entity-like; (2) force `Monster` when the candidate has a complete stat block AND a
creature-like name; (3) force `MagicItem` when the candidate has a magic-item signature; (4)
otherwise defer to the content-first union (pick-or-decline), unchanged.

#### Scenario: Non-entity-named candidate is dropped before extraction
- **WHEN** a candidate's name is not entity-like (e.g. "ACTIONS")
- **THEN** the candidate is dropped and no extraction LLM call is made for it

#### Scenario: Complete stat block with a creature-like name forces Monster
- **WHEN** a candidate has a complete stat block and a creature-like name (e.g. "Aboleth")
- **THEN** it is extracted as `Monster` directly, without being offered the decline branch

#### Scenario: Override guard — tutorial/fragment stat block is not forced Monster
- **WHEN** a candidate has stat-block-like text but a non-creature-like name (e.g. "Step 2. Basic Statistics", "Challenge 7 (2,900 XP)" from the "Creating a Monster" chapter)
- **THEN** the Monster override does NOT fire (the candidate is dropped or falls through to the union, never forced Monster)

#### Scenario: Magic-item signature forces MagicItem
- **WHEN** a candidate has a magic-item signature (e.g. Vorpal Sword: "legendary", "requires attunement")
- **THEN** it is extracted as `MagicItem`, not `Item` or a declined branch

#### Scenario: Ordinary candidate still uses content-first union
- **WHEN** a candidate is entity-like but has neither a complete stat block nor a magic-item signature (e.g. a spell, a class feature)
- **THEN** it is extracted via the content-first union pick-or-decline exactly as before

