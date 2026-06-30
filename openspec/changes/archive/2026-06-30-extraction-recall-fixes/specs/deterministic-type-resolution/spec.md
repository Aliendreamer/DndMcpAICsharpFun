## ADDED Requirements

### Requirement: Name resolution prefers the candidate's prior type on a cross-type collision

When a candidate's name matches one or more 5etools entries, the resolver SHALL prefer a match whose type
equals the candidate's primary prior type (`TypePrior[0]`). It SHALL force a cross-type match (a match of
a different type) only when no same-prior-type match exists. When the only match is cross-type and the
candidate's primary prior is a gated type, the resolver SHALL defer to content-first rather than forcing
the colliding cross-type. The stat-block monster rescue continues to take precedence over this rule.

#### Scenario: A race name colliding with a monster stays a race
- **WHEN** a candidate with primary prior Race is named "Dwarf" and 5etools has both a "Dwarf" Monster and a "Dwarf" Race
- **THEN** the resolver forces Race (the same-prior match), not Monster

#### Scenario: A spell name colliding with a non-spell heading stays a spell
- **WHEN** a candidate with primary prior Spell is named "Darkvision" and the only 5etools match of that name is a non-Spell entry
- **THEN** the resolver does not force the cross-type; it defers to content-first (and the candidate is extracted as a Spell)

#### Scenario: A genuine cross-type match with no same-prior match is unaffected
- **WHEN** a candidate's only match is cross-type and its primary prior is not a gated type
- **THEN** the existing behaviour is unchanged (the match is used / it defers as before)

#### Scenario: Stat-block rescue still wins
- **WHEN** a candidate has a complete monster stat block but its name matches a non-Monster 5etools entry
- **THEN** the monster stat-block rescue forces Monster, ahead of the prior-preference rule
