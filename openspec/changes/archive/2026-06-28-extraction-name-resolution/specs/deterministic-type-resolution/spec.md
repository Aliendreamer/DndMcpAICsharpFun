## MODIFIED Requirements

### Requirement: Entity-like name quality check

The system SHALL classify a candidate name as entity-like or not, where section headings and
stat-block sub-headers are NOT entity-like. Rejection SHALL be driven by a denylist of structural
sub-headers (`ACTIONS`, `REACTIONS`, `TRAITS`, `BONUS ACTIONS`, `LEGENDARY ACTIONS`, `LAIR ACTIONS`,
`REGIONAL EFFECTS`) plus the existing heading/fragment patterns (`Step N`, `Challenge N`, `Appendix`,
`Creating …`, `… Features`, leading-digit/no-letter) and a lair-name reject (`A …'s LAIR`). The check
SHALL NOT reject a name merely for being a single all-caps word — Marker renders entity names in
all-caps, so `FIREBALL` / `ABOLETH` / `BARD` are entity-like.

#### Scenario: Real entity name is entity-like
- **WHEN** the name is a normal entity name (e.g. "Aboleth", "Bag of Holding", "Fireball")
- **THEN** `ExtractionSignatures.IsEntityLikeName` returns true

#### Scenario: All-caps single-word entity name is entity-like
- **WHEN** the name is a single-word entity rendered all-caps by Marker (e.g. "FIREBALL", "ABOLETH", "BARD", "LION")
- **THEN** `ExtractionSignatures.IsEntityLikeName` returns true (the old all-caps rejection is removed)

#### Scenario: Structural sub-header is not entity-like
- **WHEN** the name is a stat-block sub-header on the denylist (e.g. "ACTIONS", "REACTIONS", "LEGENDARY ACTIONS")
- **THEN** `ExtractionSignatures.IsEntityLikeName` returns false

#### Scenario: Lair heading is not entity-like
- **WHEN** the name is a lair section heading (e.g. "A RED DRAGON'S LAIR")
- **THEN** `ExtractionSignatures.IsEntityLikeName` returns false

#### Scenario: Heading or fragment name is not entity-like
- **WHEN** the name is a section heading or stat-block fragment (e.g. "Appendix D: Creature Statistics", "Step 2. Basic Statistics", "Challenge 7 (2,900 XP)")
- **THEN** `ExtractionSignatures.IsEntityLikeName` returns false

### Requirement: Deterministic per-candidate type resolution before the union

The system SHALL resolve each candidate through one deterministic ladder before falling through
to content-first union extraction. The ladder SHALL, in order: (1) if the candidate's raw name
matches a 5etools entity (above the fuzzy confidence threshold), force that entity's type and use
its canonical name; (2) drop a candidate whose name is not entity-like; (3) force `Monster` when the
candidate has a complete stat block AND a creature-like name; (4) force `MagicItem` when the candidate
has a magic-item signature; (5) otherwise defer to the content-first union (pick-or-decline). A
5etools match SHALL NOT depend on the drop filter (it precedes it), so a real entity is never dropped
when 5etools knows it.

#### Scenario: 5etools match forces type and canonical name
- **WHEN** a candidate "FIREBALL" matches the 5etools spell "Fireball"
- **THEN** it is extracted as `Spell` with the canonical name "Fireball" (and entity id derived from it), skipping the LLM type-decision

#### Scenario: 5etools match precedes the drop filter
- **WHEN** a candidate's raw heading would be all-caps/single-word but matches a 5etools entity
- **THEN** it is kept and force-typed from 5etools (it is NOT dropped) — the 5etools step runs before the drop step

#### Scenario: Non-entity-named candidate is dropped before extraction
- **WHEN** a candidate's name does not match 5etools and is not entity-like (e.g. "ACTIONS")
- **THEN** the candidate is dropped and no extraction LLM call is made for it

#### Scenario: Complete stat block with a creature-like name forces Monster
- **WHEN** a candidate has no 5etools match but a complete stat block and a creature-like name
- **THEN** it is extracted as `Monster` directly, without being offered the decline branch

#### Scenario: Override guard — tutorial/fragment stat block is not forced Monster
- **WHEN** a candidate has stat-block-like text but a non-creature-like name (e.g. "Step 2. Basic Statistics" from the "Creating a Monster" chapter)
- **THEN** the Monster override does NOT fire (dropped or falls through to the union, never forced Monster)

#### Scenario: Magic-item signature forces MagicItem
- **WHEN** a candidate has no 5etools match but a magic-item signature (e.g. "legendary", "requires attunement")
- **THEN** it is extracted as `MagicItem`, not `Item` or a declined branch

#### Scenario: Unmatched ordinary candidate still uses content-first union
- **WHEN** an entity-like candidate has no 5etools match, no complete stat block, and no magic-item signature
- **THEN** it is extracted via the content-first union pick-or-decline (the safety fallback — never dropped)
