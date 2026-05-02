## MODIFIED Requirements

### Requirement: Bookmark reader returns chapter-level nodes only
The bookmark reader SHALL return only root-level nodes and their immediate children (depth 0 and depth 1). Deeper descendants SHALL be excluded to keep the TOC classification prompt manageable.

#### Scenario: Nested bookmarks return two levels
- **WHEN** a PDF has a bookmark tree with root → chapter → section hierarchy
- **THEN** the reader SHALL return root and chapter nodes only, excluding section nodes

#### Scenario: Flat bookmarks are unaffected
- **WHEN** a PDF has only root-level bookmarks with no children
- **THEN** the reader SHALL return all root nodes

### Requirement: Entity extractor unwraps single-key JSON objects
The entity extractor SHALL unwrap responses of the form `{"<key>": [...]}` (a JSON object with exactly one key whose value is an array) before attempting to parse the array. This handles the case where the model wraps the result array in an object.

#### Scenario: Wrapped array is unwrapped transparently
- **WHEN** the model returns `{"entities": [{"name": "Fireball", ...}]}`
- **THEN** the extractor SHALL parse it as if the model returned `[{"name": "Fireball", ...}]`

#### Scenario: Bare array is unchanged
- **WHEN** the model returns a bare JSON array `[{"name": "Fireball", ...}]`
- **THEN** the extractor SHALL parse it directly without modification

### Requirement: TOC and entity prompts use full category list
Both the TOC classifier system prompt and the entity extractor system prompt SHALL list all valid categories including the six new values. The TOC prompt SHALL include concrete mapping examples for the new categories.

#### Scenario: TOC prompt includes new categories
- **WHEN** the TOC classifier is invoked
- **THEN** its system prompt SHALL list `God`, `Combat`, `Adventuring`, `Condition`, `Plane`, `Race` as valid categories alongside existing ones
