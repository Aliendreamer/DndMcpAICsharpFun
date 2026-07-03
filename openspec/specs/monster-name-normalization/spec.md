# monster-name-normalization Specification

## Purpose
TBD - created by archiving change mm-monster-name-and-precision. Update Purpose after archive.
## Requirements
### Requirement: Stat-line suffix stripping for monster name matching

Monster name matching SHALL strip a trailing stat-line suffix — `"<Size> <creature-type>[, <alignment> ...]"`
where Size is one of Tiny/Small/Medium/Large/Huge/Gargantuan and creature-type is one of the D&D creature
types (aberration, beast, celestial, construct, dragon, elemental, fey, fiend, giant, humanoid, monstrosity,
ooze, plant, undead, swarm) — from a raw heading before normalization, so a stat-line-garbled heading
resolves to its clean 5etools canonical name. The stripper MUST only remove a suffix that follows real name
text and MUST NOT return an empty name; a heading with no stat-line suffix is returned unchanged.

#### Scenario: Garbled dragon heading matches its clean 5etools name

- **WHEN** the matcher is given `ANCIENT BLACK DRAGON Gargantuan dragon, chaotic evil`
- **THEN** it strips the `Gargantuan dragon, chaotic evil` suffix, matches the 5etools `Ancient Black Dragon`,
  and returns that canonical name and type Monster

#### Scenario: A name without a stat-line suffix is unchanged

- **WHEN** the matcher is given a clean name such as `Dragon Turtle` or `Giant Ape` (a size word not
  followed by a creature type) or `Beholder`
- **THEN** no suffix is stripped and matching behaves as before

#### Scenario: Grounded stat-line-named monster is not duplicated by backfill

- **WHEN** a monster was extracted grounded but with a stat-line-garbled name, and its clean name exists in
  the 5etools roster
- **THEN** after re-extraction it carries the clean canonical name, is reported present (not missing) by the
  recall check, and is NOT backfilled as a duplicate

