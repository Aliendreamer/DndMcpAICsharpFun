## ADDED Requirements

### Requirement: Candidate-name matching SHALL strip a trailing magic-item stat line before lookup

`EntityNameMatcher` SHALL strip a trailing magic-item stat line from the raw candidate name before normalising and looking it up in the 5etools index, in addition to the existing monster stat-line strip. Without this, a concatenated heading such as `"AMULET OF THE DRUNKARD Wondrous item, uncommon"` normalises to a key that exists in no index and matches nothing, so the candidate loses its grounding and its stable entity id.

Both strips SHALL be applied, and the strip SHALL run **before** the match, because `DeterministicTypeResolver` never drops a name that matches a 5etools entry — a match found on the stripped name is what protects a real magic item from being dropped by the name gate.

#### Scenario: A concatenated magic-item heading grounds to its 5etools entry

- **WHEN** the raw candidate name is `"AMULET OF THE DRUNKARD Wondrous item, uncommon"` and 5etools contains the `Amulet of the Drunkard` magic item
- **THEN** the matcher strips the trailing stat line and returns that entry, so the candidate is grounded and receives the canonical name and a stable id

#### Scenario: Monster stat-line stripping is unaffected

- **WHEN** the raw candidate name carries a monster stat-line suffix, e.g. `"Moorbounder Large monstrosity, unaligned"`
- **THEN** the existing monster strip still applies and the name resolves as before

#### Scenario: A name that is only a stat line matches nothing

- **WHEN** the raw candidate name is `"Wondrous item, uncommon"`
- **THEN** stripping leaves the input unchanged, the lookup finds no entry, and the candidate is left unmatched so the name gate can drop it
