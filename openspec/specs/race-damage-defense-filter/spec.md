# race-damage-defense-filter Specification

## Purpose
TBD - created by archiving change race-damage-defense-filter. Update Purpose after archive.
## Requirements
### Requirement: Parse a race's resisted/immune damage types from strings and choose-blocks

The system SHALL provide a parser that, given a race's raw entity fields and a key (`resist` or `immune`), returns the set of damage-type codes the race resists / is immune to — including both direct string entries and each entry of a `choose.from` block. A race with no data for that key SHALL yield an empty set. The parser SHALL NOT throw on missing or unexpected shapes and SHALL match the key case-insensitively.

#### Scenario: Direct damage-type strings

- **WHEN** a race's `resist` is `["necrotic","radiant"]`
- **THEN** the resist set is `{necrotic, radiant}`

#### Scenario: Choosable resistance is included

- **WHEN** a race's `resist` is `[{"choose":{"from":["acid","cold","fire","lightning","poison"]}}]`
- **THEN** the resist set includes `fire` (and the other listed types)

#### Scenario: Immune reads its own key

- **WHEN** a race's `immune` is `["poison"]`
- **THEN** the immune set is `{poison}` and its resist set (absent) is empty

#### Scenario: No data is empty, never throws

- **WHEN** a race has no `resist`/`immune` (or a malformed value)
- **THEN** the set is empty and no exception is raised

### Requirement: Filter entity retrieval by race damage resistance and immunity

`GET /retrieval/entities/list` SHALL accept `resist` and `immune` query parameters (damage-type codes, case-insensitive). When either is present, the retrieval SHALL return only Race entities matching, at query time (not via a payload index): the race's resist set contains the `resist` code (when given) AND its immune set contains the `immune` code (when given). An unset parameter SHALL not constrain; both set SHALL AND. The result total SHALL be the matched count. An unrecognized value SHALL yield zero matches without error. When neither is present, retrieval behavior SHALL be unchanged.

#### Scenario: Races that resist a damage type are returned

- **WHEN** `resist=fire` runs and a race resists fire (fixed or via a choose-block)
- **THEN** that race is in the results; a race that does not resist fire is excluded and not counted

#### Scenario: Immune filter is independent

- **WHEN** `immune=poison` runs
- **THEN** only races immune to poison are returned (a race that merely resists poison is not)

#### Scenario: Both parameters AND

- **WHEN** `resist=fire&immune=poison` runs
- **THEN** only races that both resist fire AND are immune to poison are returned

#### Scenario: Case-insensitive and unchanged-when-absent

- **WHEN** `resist=FIRE` runs
- **THEN** it behaves identically to `resist=fire`; and a request with neither `resist` nor `immune` filters exactly as before this change

