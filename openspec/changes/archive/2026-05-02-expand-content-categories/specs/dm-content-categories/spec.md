## MODIFIED Requirements

### Requirement: ContentCategory enum includes all D&D content types
The `ContentCategory` enum SHALL contain the following values: `Spell`, `Monster`, `Class`, `Background`, `Item`, `Rule`, `Treasure`, `Encounter`, `Trap`, `God`, `Combat`, `Adventuring`, `Condition`, `Plane`, `Race`.

#### Scenario: All new values parse correctly
- **WHEN** a string value of `"God"`, `"Combat"`, `"Adventuring"`, `"Condition"`, `"Plane"`, or `"Race"` is parsed as `ContentCategory`
- **THEN** `Enum.TryParse` SHALL return `true` and the correct enum value

#### Scenario: Existing values are unchanged
- **WHEN** a string value matching any pre-existing category is parsed
- **THEN** it SHALL continue to parse to the same enum value as before
