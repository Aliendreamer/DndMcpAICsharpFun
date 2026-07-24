## ADDED Requirements

### Requirement: Parse a race's boosted abilities from fixed and choosable bonuses

The system SHALL provide a parser that, given a race's structured ability data (`RaceFields.Ability`), returns the set of ability codes (`str`, `dex`, `con`, `int`, `wis`, `cha`) the race boosts — including both fixed bonuses (a numeric ability key) and choosable bonuses (each entry of a `choose.from` list). A race with no structured ability data SHALL yield an empty set. The parser SHALL NOT throw on missing or unexpected shapes.

#### Scenario: Fixed bonus

- **WHEN** a race's ability data is `{"str":2,"con":1}`
- **THEN** the boosted set is `{str, con}`

#### Scenario: Choosable bonus is included

- **WHEN** a race's ability data is `{"choose":{"from":["str","dex","con"],"count":1}}`
- **THEN** the boosted set includes `str`, `dex`, and `con`

#### Scenario: Mixed fixed and choosable

- **WHEN** a race's ability data is `{"cha":2,"choose":{"from":["str","wis"],"count":1}}`
- **THEN** the boosted set is `{cha, str, wis}`

#### Scenario: No structured ability data

- **WHEN** a race has no `Ability` data (null or empty)
- **THEN** the boosted set is empty (the race matches no ability filter; nothing is fabricated)

### Requirement: Filter entity retrieval by race ability bonus

`GET /retrieval/entities/list` SHALL accept an `abilityBonus` query parameter (an ability code, case-insensitive). When present, the retrieval SHALL return only Race entities whose boosted-ability set contains that code, matched at query time (not via a payload index), and the result's total SHALL be the matched count. An unrecognized `abilityBonus` value SHALL yield zero matches without error. When `abilityBonus` is absent, retrieval behavior SHALL be unchanged.

#### Scenario: Races with a fixed Strength bonus are returned

- **WHEN** `GET /retrieval/entities/list?abilityBonus=str` runs and a race grants a fixed `{"str":2}`
- **THEN** that race is in the results

#### Scenario: A race with a choosable Strength option is returned

- **WHEN** `abilityBonus=str` runs and a race grants `{"choose":{"from":["str","dex"],"count":1}}`
- **THEN** that race is in the results (choosable counts)

#### Scenario: A race without the ability is excluded

- **WHEN** `abilityBonus=str` runs and a race grants only `{"dex":2}`
- **THEN** that race is NOT in the results, and it is not counted in the total

#### Scenario: Case-insensitive and unchanged-when-absent

- **WHEN** `abilityBonus=STR` runs
- **THEN** it behaves identically to `abilityBonus=str`; and a request with no `abilityBonus` filters exactly as before this change
