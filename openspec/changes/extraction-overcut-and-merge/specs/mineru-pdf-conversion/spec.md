## ADDED Requirements

### Requirement: Spell-name extraction preserves a school word in the name

The converter SHALL extract a promoted spell name by stripping only the trailing level/school suffix
(`"<Nth-level> <school>"` or `"<school> cantrip"`), so a spell whose name itself contains a school word
is preserved. It MUST NOT cut the name at the first school word when that word is part of the name.

#### Scenario: A cantrip whose name ends in a school word
- **WHEN** the promoted block is "MINOR ILLUSION Illusion cantrip"
- **THEN** the extracted name is "MINOR ILLUSION" (only the trailing "Illusion cantrip" suffix removed)

#### Scenario: Another school-word-name cantrip
- **WHEN** the promoted block is "PROGRAMMED ILLUSION Illusion ..." with an illusion/cantrip suffix
- **THEN** the extracted name is "PROGRAMMED ILLUSION", not "PROGRAMMED"

#### Scenario: A leveled spell is cut at the digit (unchanged)
- **WHEN** the promoted block is "SHIELD OF FAITH 1st-level abjuration"
- **THEN** the extracted name is "SHIELD OF FAITH" (cut at the level digit)
