## ADDED Requirements

### Requirement: Object entity type for AC/HP-bearing non-creatures

The system SHALL provide an `Object` entity type representing D&D objects that have combat statistics but are not creatures (siege weapons, suspended cauldrons, animated doors/statues). `EntityType.Object` SHALL exist in the domain enum, and an `ObjectFields` schema SHALL define the object's structured fields.

#### Scenario: Siege weapon extracts as an Object with stats

- **WHEN** a book candidate describes a ballista, cannon, or ram with an Armor Class and Hit Points
- **THEN** it is extracted as an `Object` entity whose fields include the Armor Class, Hit Points, and any attack action, and whose `canonicalText` renders those stats

#### Scenario: Object is not persisted as an empty Item shell

- **WHEN** an AC/HP-bearing non-creature candidate is extracted
- **THEN** it is NOT produced as an `Item` entity with empty `fields`

### Requirement: ObjectFields captures object combat statistics

`ObjectFields` SHALL model the subset of creature statistics that objects use: armor class, hit points, damage immunities/resistances/vulnerabilities, condition immunities, an optional list of attack actions (name, attack bonus, damage, reach or range), and a short description. The canonical JSON schema SHALL be generated for `ObjectFields` alongside the other field types.

#### Scenario: Object attack action is preserved

- **WHEN** an object candidate has an attack (e.g. a ballista's "+6 to hit, 3d10 piercing")
- **THEN** the extracted `Object` records that action with its attack bonus and damage

#### Scenario: Schema is available for validation and ingestion

- **WHEN** the canonical schemas are generated
- **THEN** an `ObjectFields` schema exists and `Object` entities validate against it

### Requirement: Object type is rendered to canonical text

The system SHALL render an `Object` entity to `canonicalText` including its Armor Class, Hit Points, immunities, and attack actions, via a dedicated renderer wired into the canonical-text dispatch.

#### Scenario: Object canonical text includes combat stats

- **WHEN** an `Object` entity is rendered to canonical text
- **THEN** the text states its Armor Class and Hit Points and lists its attack actions

### Requirement: Object type is not 5etools-gated

The `Object` type SHALL NOT be a 5etools-gated type. `Object` candidates SHALL be extracted by the LLM and SHALL NOT be declined for lack of a match in the 5etools monster index. No 5etools grounding or backfill is applied to `Object` entities.

#### Scenario: Object candidate is not declined for missing monster match

- **WHEN** a siege-weapon candidate has no entry in the 5etools monster index
- **THEN** it is still extracted as an `Object` and is NOT written to `declined.json` as a no-5etools-match decline
