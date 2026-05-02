# dm-content-categories

## Purpose

Defines the `ContentCategory` enum and detection requirements for D&D content categories, including the DM-style source book categories (Treasure, Encounter, Trap) and the six expanded prose-only categories (God, Combat, Adventuring, Condition, Plane, Race).

## Requirements

### Requirement: ContentCategory enum includes all D&D content types
The `ContentCategory` enum SHALL contain the following values: `Spell`, `Monster`, `Class`, `Background`, `Item`, `Rule`, `Treasure`, `Encounter`, `Trap`, `God`, `Combat`, `Adventuring`, `Condition`, `Plane`, `Race`.

#### Scenario: All new values parse correctly
- **WHEN** a string value of `"God"`, `"Combat"`, `"Adventuring"`, `"Condition"`, `"Plane"`, or `"Race"` is parsed as `ContentCategory`
- **THEN** `Enum.TryParse` SHALL return `true` and the correct enum value

#### Scenario: Existing values are unchanged
- **WHEN** a string value matching any pre-existing category is parsed
- **THEN** it SHALL continue to parse to the same enum value as before

### Requirement: Treasure chunks are detected from hoard and table content
The system SHALL assign `ContentCategory.Treasure` to chunks that contain at least 70% of the defined treasure signals (`"Treasure Hoard"`, `"Art Objects"`, `"Gemstones"`).

#### Scenario: Hoard table chunk is classified as Treasure
- **WHEN** a chunk contains `"Treasure Hoard"`, `"Art Objects"`, and `"Gemstones"`
- **THEN** `ContentCategoryDetector.Detect` returns `ContentCategory.Treasure`

#### Scenario: Chunk with no treasure signals falls to chapter default
- **WHEN** a chunk contains none of the treasure signals
- **THEN** `ContentCategoryDetector.Detect` returns the supplied `chapterDefault`

#### Scenario: Treasure Hoard line marks entity boundary
- **WHEN** `IsEntityBoundary` is called with a line containing `"Treasure Hoard"`
- **THEN** it returns `true`

### Requirement: Encounter chunks are detected from encounter-building content
The system SHALL assign `ContentCategory.Encounter` to chunks that contain at least 70% of the defined encounter signals (`"Encounter Difficulty"`, `"XP Threshold"`, `"Random Encounter"`).

#### Scenario: Encounter difficulty chunk is classified as Encounter
- **WHEN** a chunk contains `"Encounter Difficulty"`, `"XP Threshold"`, and `"Random Encounter"`
- **THEN** `ContentCategoryDetector.Detect` returns `ContentCategory.Encounter`

#### Scenario: Random Encounter line marks entity boundary
- **WHEN** `IsEntityBoundary` is called with a line containing `"Random Encounter"`
- **THEN** it returns `true`

### Requirement: Trap chunks are detected from trap description content
The system SHALL assign `ContentCategory.Trap` to chunks that contain at least 70% of the defined trap signals (`"Trigger:"`, `"Effect:"`, `"Disarm DC:"`).

#### Scenario: Full trap description is classified as Trap
- **WHEN** a chunk contains `"Trigger:"`, `"Effect:"`, and `"Disarm DC:"`
- **THEN** `ContentCategoryDetector.Detect` returns `ContentCategory.Trap`

#### Scenario: Trigger line marks entity boundary
- **WHEN** `IsEntityBoundary` is called with a line containing `"Trigger:"`
- **THEN** it returns `true`
