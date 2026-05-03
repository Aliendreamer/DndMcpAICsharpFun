## MODIFIED Requirements

### Requirement: Entity extractor unwraps single-key JSON objects
The entity extractor SHALL unwrap responses of the form `{"<key>": [...]}` (a JSON object with exactly one key whose value is an array) before attempting to parse the array. It SHALL also wrap a single JSON object response `{"name":..., "partial":..., "data":...}` into a one-element array. This handles the case where the model wraps the result array in an object or returns a single entity.

#### Scenario: Wrapped array is unwrapped transparently
- **WHEN** the model returns `{"entities": [{"name": "Fireball", ...}]}`
- **THEN** the extractor SHALL parse it as if the model returned `[{"name": "Fireball", ...}]`

#### Scenario: Bare array is unchanged
- **WHEN** the model returns a bare JSON array `[{"name": "Fireball", ...}]`
- **THEN** the extractor SHALL parse it directly without modification

#### Scenario: Single object response is wrapped into array
- **WHEN** the model returns a single JSON object `{"name": "d4", "partial": false, "data": {...}}`
- **THEN** the extractor SHALL treat it as a one-element array

## ADDED Requirements

### Requirement: LLM extraction prompt input is formatted with heading context
The system SHALL build the LLM prompt input by formatting structured page blocks as `[H1] text`, `[H2] text`, `[H3] text`, or plain `text` lines (for body blocks), joined with newlines. The raw page text string is not used as prompt input.

#### Scenario: Heading blocks are prefixed with level tags
- **WHEN** a page has blocks `[{level:"h2", text:"Totem Warrior"}, {level:"h3", text:"Bear"}, {level:"body", text:"While raging..."}]`
- **THEN** the prompt input SHALL be `"[H2] Totem Warrior\n[H3] Bear\nWhile raging..."`

#### Scenario: Body-only page produces plain text prompt
- **WHEN** all blocks on a page have level `body`
- **THEN** the prompt input SHALL contain no `[Hx]` prefixes
