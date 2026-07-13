## ADDED Requirements

### Requirement: NPC generation anchors to a real corpus stat block

The system SHALL resolve a caller-supplied NPC archetype to a real Monster entity in the corpus by an
exact (normalized) name match and return that entity's grounded stat block (its name, source book
citation, challenge rating, armour class, hit points, ability scores, and its rendered stat-block
text). It SHALL NOT return an unrelated entity when the archetype does not match. When the archetype
does not resolve, the system SHALL report that it is not in the corpus and provide a list of available
NPC archetypes to choose from instead.

#### Scenario: A valid archetype returns its grounded stat block

- **WHEN** NPC generation is asked for an archetype that exists in the corpus (e.g. Spy)
- **THEN** it SHALL return that entity's real stat block (challenge rating, armour class, hit points, ability scores, and rendered stat-block text) with its source citation

#### Scenario: An unknown archetype is reported with alternatives

- **WHEN** the supplied archetype does not match any Monster entity
- **THEN** the system SHALL report it is not in the corpus and return a list of available NPC archetypes, and SHALL NOT return an unrelated stat block

#### Scenario: A challenge-rating cap rejects an over-powered archetype

- **WHEN** a maximum challenge rating is supplied and the resolved archetype's challenge rating exceeds it
- **THEN** the system SHALL NOT return that archetype as the grounded pick, and SHALL return the available-archetypes list so a weaker one can be chosen

### Requirement: Grounded NPC generation tool with an invent-only-flavour contract

The system SHALL expose a per-session chat tool `generate_npc(concept, archetype, maxCr?)` that is not
ownership-gated and does not accept a user or campaign id. Its contract SHALL require the mechanical
stats to come only from the returned stat block (cited), while the NPC's name, personality,
appearance, and hook are composed to fit the concept — the caller SHALL NOT invent stat numbers, and
SHALL pick from the returned available archetypes when the chosen one is not in the corpus.

#### Scenario: Tool returns grounded stats for the persona to reskin

- **WHEN** `generate_npc` is called with a concept and a fitting archetype in the corpus
- **THEN** it SHALL return the archetype's real stat block, and the contract SHALL require the answer's stats to come from that block (cited) while the name/personality/appearance/hook are invented to fit the concept

#### Scenario: Tool schema does not expose a user or campaign id

- **WHEN** the `generate_npc` tool schema is inspected
- **THEN** it SHALL NOT expose a `userId` or `campaignId` argument
