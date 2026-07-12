# encounter-design Specification

## Purpose
TBD - created by archiving change encounter-design. Update Purpose after archive.
## Requirements
### Requirement: Deterministic encounter math for both editions

The system SHALL provide a pure, table-driven `EncounterMath` that computes encounter difficulty for
both the 2014 and 2024 rulesets, selected by an edition parameter, using a shared CR→XP table. For
2014 it SHALL sum per-character Easy/Medium/Hard/Deadly XP thresholds over the party and apply an
encounter multiplier determined by the number of monsters, then classify `sum(monsterXP) ×
multiplier` against the thresholds. For 2024 it SHALL sum per-character Low/Moderate/High XP budgets
over the party and classify the raw `sum(monsterXP)` against the budgets, with NO multiplier. The
math SHALL be a pure function of its inputs with no I/O.

#### Scenario: CR maps to the standard XP value

- **WHEN** a monster's challenge rating is looked up
- **THEN** it SHALL map to the standard D&D XP value for that CR (e.g. CR 1 → 200 XP, CR 5 → 1,800 XP)

#### Scenario: 2014 applies the count-based multiplier

- **WHEN** difficulty is computed for the 2014 edition with more than one monster
- **THEN** the summed monster XP SHALL be multiplied by the encounter multiplier for that monster count before it is compared to the party thresholds

#### Scenario: 2024 applies no multiplier

- **WHEN** difficulty is computed for the 2024 edition
- **THEN** the raw summed monster XP SHALL be compared to the party budgets with no multiplier applied

#### Scenario: Party-size multiplier shift (2014)

- **WHEN** the 2014 party has fewer than 3 or 6-or-more characters
- **THEN** the effective multiplier SHALL shift one band up (fewer than 3) or one band down (6 or more) from the by-count multiplier

### Requirement: Assessor rates a given encounter

The system SHALL provide an `EncounterAssessor` that, given the party levels, a list of monsters
(each resolved to a CR/XP), and an edition, returns the total monster XP, the 2014-adjusted XP where
applicable, the resulting difficulty band, and the surrounding threshold/budget values for context.

#### Scenario: Correct band for a given encounter

- **WHEN** the assessor is given a party and a monster list
- **THEN** it SHALL return the difficulty band that the edition's math produces for that combination, along with the total (and 2014-adjusted) XP

#### Scenario: Context thresholds are reported

- **WHEN** the assessor returns a difficulty band
- **THEN** it SHALL also report the neighbouring band boundaries for that party (e.g. the XP at which the next-harder band begins)

### Requirement: Generator builds an encounter and rates it with the same math

The system SHALL provide an `EncounterGenerator` that, given the party, a target difficulty, an
edition, and optional theme/CR constraints, selects monsters from `dnd_entities` via the existing
entity search (`type=Monster`, `crNumeric` band, optional `keyword`, `srd`, edition) and returns the
proposed monsters together with the difficulty produced by running them through the `EncounterAssessor`.
The returned difficulty SHALL be the Assessor's verdict, so a built encounter is never rated
differently than the same monsters passed directly to the Assessor. For 2014 the generator SHALL
account for the count→multiplier feedback loop by targeting the assessed band rather than raw XP.

The generator SHALL support monster *quantity* (swarms) by selecting the same candidate monster more
than once. It SHALL build a boss-plus-minions shape: the first selection is the single
highest-adjusted-XP candidate that does not overshoot the target band (the anchor), after which the
generator SHALL fill toward the target band by re-selecting greedily from candidates whose per-monster
XP is strictly less than the anchor's (the minions). When no candidate is strictly cheaper than the
anchor, the generator SHALL re-select from the anchor tier itself rather than dead-ending. The result
SHALL be returned as a flat monster list in which quantity is expressed as repeated monster entries,
so each member is an individual combatant and the count-based math applies unchanged. The existing
`MaxMonsters` cap and the not-fully-matched fallback flag/note SHALL be preserved.

#### Scenario: Built encounter lands in the requested band

- **WHEN** the generator is asked to build an encounter of a given difficulty and monsters are available in range
- **THEN** the returned monsters SHALL assess (via the Assessor) to the requested difficulty band

#### Scenario: Build and rate never disagree

- **WHEN** the generator returns a built encounter with a difficulty
- **THEN** passing those exact monsters and party to the Assessor SHALL yield the same difficulty band

#### Scenario: Theme and CR constraints are honoured

- **WHEN** a theme keyword or CR constraint is supplied
- **THEN** the retrieved candidate monsters SHALL be filtered by those constraints via the existing entity search

#### Scenario: Graceful fallback when nothing fits

- **WHEN** no monster set can be assembled that lands in the requested band (e.g. the theme/CR corpus is too sparse)
- **THEN** the generator SHALL return the closest set it could assemble, explicitly flagged as not fully matching the target, rather than a silent under-budget encounter presented as on-target

#### Scenario: Swarm is built as an anchor plus cheaper multiples

- **WHEN** the generator builds toward a band that a single anchor cannot fill and cheaper candidates exist
- **THEN** the result SHALL contain one anchor monster (the single most expensive selection) and multiple copies of one or more strictly-cheaper monsters, expressed as repeated entries in the flat list

#### Scenario: Anchor alone suffices

- **WHEN** the single highest-XP-under-target candidate already reaches the target band
- **THEN** the generator SHALL return that solo anchor with no minions

#### Scenario: Uniform swarm when no cheaper candidate exists

- **WHEN** every candidate shares the anchor's per-monster XP (no strictly-cheaper minion tier)
- **THEN** the generator SHALL fill the band with multiple copies of that tier rather than returning only the anchor

#### Scenario: Swarm build and rate never disagree

- **WHEN** the generator returns a swarm containing repeated monster entries
- **THEN** rating those exact repeated monsters and party via the Assessor SHALL yield the same difficulty band

### Requirement: Per-user encounter tools with campaign-scoped party resolution

The system SHALL expose two per-user chat tools, `rate_encounter` and `build_encounter`, that close
over the authenticated session user id and never accept a user or campaign id as a trusted argument
across the shared-key boundary. When a `campaignId` is given, the party SHALL be resolved from that
campaign's heroes' levels only after verifying the campaign belongs to the caller; a campaign that is
not the caller's SHALL be rejected. An explicit `partyLevels` argument SHALL override the campaign
party for hypothetical parties. When neither `campaignId` nor `partyLevels` is supplied, the tool
SHALL return a clear error rather than guess.

The `rate_encounter` tool SHALL accept its monsters as structured `{name, quantity}` pairs. Each pair
SHALL be resolved once and contribute `quantity` copies of the resolved monster to the rated set; a
quantity that is absent or not positive SHALL be treated as one, and the per-pair copy count SHALL be
clamped to a fixed safety maximum so a single pair cannot balloon the rated set. The `build_encounter`
tool SHALL echo its result grouped by monster with counts (e.g. "8× Goblin") rather than listing each
repeated monster separately.

#### Scenario: Party resolved from the caller's own campaign

- **WHEN** `build_encounter` is called with a `campaignId` the caller owns
- **THEN** the party SHALL be the levels of that campaign's heroes

#### Scenario: Another user's campaign is rejected

- **WHEN** a tool is called with a `campaignId` that belongs to a different user
- **THEN** the call SHALL be rejected (unauthorized) and SHALL NOT reveal that campaign's party

#### Scenario: Explicit party overrides the campaign

- **WHEN** `partyLevels` is supplied
- **THEN** the encounter SHALL be computed for that explicit party regardless of any campaign

#### Scenario: Missing party is an explicit error

- **WHEN** neither `campaignId` nor `partyLevels` is supplied
- **THEN** the tool SHALL return a clear error asking for a party, not a guessed default

#### Scenario: Rate accepts monster quantities

- **WHEN** `rate_encounter` is called with a `{name, quantity}` pair such as `{ "goblin", 8 }`
- **THEN** the rated set SHALL contain eight copies of the resolved goblin and be assessed with the count-based math

#### Scenario: Non-positive quantity defaults to one

- **WHEN** a `{name, quantity}` pair has a zero or negative quantity
- **THEN** the pair SHALL contribute exactly one copy of the resolved monster

#### Scenario: Excessive quantity is clamped

- **WHEN** a `{name, quantity}` pair requests more copies than the safety maximum
- **THEN** the contributed copies SHALL be clamped to that maximum rather than expanding without bound

