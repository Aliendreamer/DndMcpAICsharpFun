## ADDED Requirements

### Requirement: Rendered entity text carries all printed text

Every entity type's rendered `canonicalText` SHALL contain the text of all its `fields.entries` and of every other text-bearing field, not only the first paragraph.

#### Scenario: A race with several traits

- **WHEN** a Race whose `entries` hold several paragraphs and named traits is rendered
- **THEN** every paragraph and every trait's name and text SHALL be in `canonicalText`

### Requirement: Long text is embedded in pieces

The system SHALL split an entity's text into pieces of at most 1,500 characters with overlap, SHALL embed every piece, and SHALL store all vectors in the entity's single point. A Monster's pieces SHALL also include the Accepted Lore of the same book with the same name.

#### Scenario: Long entity

- **WHEN** an entity's text is 5,000 characters long
- **THEN** its point SHALL hold one vector per piece and every character of the text SHALL be in some piece

#### Scenario: Monster with lore

- **WHEN** a Monster and an Accepted Lore entity of the same book share a name
- **THEN** the Monster's vectors SHALL include the lore's pieces and its `canonicalText` SHALL NOT include them

### Requirement: search_dnd returns a wide window around the best piece

`search_dnd` SHALL return up to 3,000 characters per result; longer text SHALL be cut to a window that contains the best-ranked piece.

#### Scenario: Match deep inside a long entity

- **WHEN** the matching paragraph starts at character 6,000 of an entity
- **THEN** the returned text SHALL contain that paragraph and SHALL be at most 3,000 characters plus ellipses

### Requirement: The chat sets its model context size

The chat SHALL send `num_ctx` from `Ollama:ChatContextTokens` (default 8192) with every request and SHALL keep the model's reasoning mode unchanged.

#### Scenario: Chat request

- **WHEN** a chat turn runs
- **THEN** the request options SHALL carry `num_ctx` 8192 and SHALL NOT set `think`
