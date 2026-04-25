## ADDED Requirements

### Requirement: Chunks are split on D&D entity boundaries
The system SHALL detect entity-start patterns (spell header, monster stat block header, background entry header) in extracted text and begin a new chunk at each detected boundary.

#### Scenario: Spell entry starts a new chunk
- **WHEN** the chunker encounters text matching the spell header pattern ("Casting Time:" within proximity of "Range:" and "Components:")
- **THEN** a new chunk boundary is inserted before the spell name line

#### Scenario: Monster stat block starts a new chunk
- **WHEN** the chunker encounters text matching the stat block pattern ("Armor Class:" within proximity of "Hit Points:" and "Speed:")
- **THEN** a new chunk boundary is inserted before the monster name line

#### Scenario: Background entry starts a new chunk
- **WHEN** the chunker encounters text matching the background pattern ("Skill Proficiencies:" followed by "Feature:")
- **THEN** a new chunk boundary is inserted before the background name line

### Requirement: Oversized entity chunks are sub-split with overlap
The system SHALL sub-split any chunk exceeding `IngestionOptions:MaxChunkTokens` at the nearest sentence boundary, with a token overlap of `IngestionOptions:OverlapTokens` between sub-chunks.

#### Scenario: Long monster description is sub-split
- **WHEN** a monster stat block chunk exceeds MaxChunkTokens
- **THEN** it is split into two or more sub-chunks, each overlapping by OverlapTokens tokens with the next

### Requirement: Text with no detected entity boundaries falls back to fixed-size chunking
The system SHALL apply fixed-size chunking at sentence boundaries for text regions where no entity patterns are detected.

#### Scenario: Narrative rules text is chunked at fixed size
- **WHEN** extracted text contains no spell, monster, or background patterns
- **THEN** chunks are produced at MaxChunkTokens size at the nearest sentence boundary

### Requirement: Each chunk carries a ChunkMetadata record
The system SHALL attach a `ChunkMetadata` value object to every chunk containing: `SourceBook`, `Version`, `Category`, `EntityName`, `Chapter`, `PageNumber`, `ChunkIndex`.

#### Scenario: Spell chunk has correct metadata
- **WHEN** a chunk is classified as a spell
- **THEN** its metadata has `Category = "spell"`, `EntityName` set to the spell name, and `Version` matching the registered book version

### Requirement: Category is detected per chunk using layered pattern matching
The system SHALL classify each chunk using chapter context as a default and pattern detectors as overrides, with `"rule"` as the ultimate fallback.

#### Scenario: Chunk inside spell chapter with no pattern match defaults to spell
- **WHEN** a chunk is inside a chapter identified as spell content but no spell pattern fires with confidence ≥ 0.7
- **THEN** the category defaults to the chapter context category

#### Scenario: Monster pattern fires in non-monster chapter
- **WHEN** a chunk matches the monster pattern with confidence ≥ 0.7 regardless of current chapter context
- **THEN** the category is set to `"monster"`, overriding the chapter default

### Requirement: Entity name is extracted from the first heading-like line of a chunk
The system SHALL extract the entity name as the first non-empty line of a chunk that precedes a known anchor pattern (e.g. "Casting Time:", "Armor Class:", "Skill Proficiencies:").

#### Scenario: Spell name is extracted
- **WHEN** a spell chunk begins with "Fireball\n3rd-level evocation\nCasting Time: ..."
- **THEN** `EntityName` is set to `"Fireball"`
