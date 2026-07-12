## ADDED Requirements

### Requirement: Ownership-gated build-critique returns grounded findings

The system SHALL, for a hero snapshot the calling user owns, compute a build-critique as a set of
deterministic findings — each anchored to a concrete fact about the character's actual sheet and, where
relevant, a real cited rule entity — and return them as a findings package the assistant frames into a
critique. It SHALL authorize by the calling user's identity: it SHALL resolve only a snapshot owned by
that user and SHALL reject a request for another user's snapshot rather than act on it. The assistant's
critique SHALL be anchored to the returned findings and SHALL NOT free-judge the build.

#### Scenario: Critiquing another user's hero is rejected

- **WHEN** the service is asked to critique a hero snapshot owned by a different user
- **THEN** it SHALL throw rather than return that hero's critique

#### Scenario: A clean build yields no findings

- **WHEN** the critique runs on a fully-recorded, consistent build
- **THEN** no findings SHALL be produced (the critique does not invent problems)

### Requirement: Untaken-choices findings

The system SHALL flag choices the character's level opens but the sheet does not record: a subclass not
chosen when the class's subclass-selection level has passed, and class features the character should have
by their level (parsed from the class's per-level features) that are absent from the sheet's recorded
features. Feature names SHALL be compared with normalization so a formatting difference does not
false-positive as missing.

#### Scenario: Missing class feature

- **WHEN** a level-5 Fighter's sheet does not list "Extra Attack"
- **THEN** an untaken-choice finding SHALL report the missing feature, citing the class rule

#### Scenario: Subclass not chosen

- **WHEN** a character's class level has passed its subclass-selection level and no subclass is recorded
- **THEN** a finding SHALL report the missing subclass choice

#### Scenario: A formatting variant is not a false positive

- **WHEN** the sheet records a feature under a slightly different formatting than the rule text
- **THEN** the feature SHALL be treated as present (no "missing" finding)

### Requirement: Stat-consistency findings

The system SHALL compute the character's spell save DC, spell attack bonus, and spell slots and compare
them to the values recorded on the sheet; a discrepancy SHALL be a finding stating both the recorded and
the computed value.

#### Scenario: Recorded save DC disagrees with the computed value

- **WHEN** the sheet's recorded spell save DC differs from the value computed from the character's ability and proficiency
- **THEN** a stat-consistency finding SHALL report both values

### Requirement: Ability-alignment findings

For a spellcasting class, the system SHALL read the class's spellcasting ability and flag when the
character's highest ability score is not that ability. Non-spellcasting classes SHALL be skipped for this
check.

#### Scenario: Casting stat is not the highest score

- **WHEN** a caster's spellcasting ability is not the character's highest ability score
- **THEN** an ability-alignment finding SHALL report the mismatch

#### Scenario: Non-caster is not flagged for casting ability

- **WHEN** the character's class has no spellcasting ability
- **THEN** no ability-alignment finding SHALL be produced for it

### Requirement: A per-user chat tool and a HeroDetail card, ownership-gated

The system SHALL expose a per-user chat tool `critique_build(heroSnapshotId)` in the authenticated tool
set that closes over the signed-in user's identity (no user-id argument), and a HeroDetail "Review this
build" action that renders the deterministic findings and continues the framed critique in the chat
surface. Unauthenticated callers SHALL NOT be offered the tool.

#### Scenario: The critique is available to the owner and takes no user id

- **WHEN** an authenticated user asks the assistant to critique their hero
- **THEN** the `critique_build` tool SHALL be available and its schema SHALL NOT expose a user-id argument

#### Scenario: HeroDetail shows the findings and hands off to chat

- **WHEN** the owner triggers "Review this build" on HeroDetail
- **THEN** the page SHALL render the deterministic findings and offer an action that continues the critique in chat
