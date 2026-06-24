## MODIFIED Requirements

### Requirement: LLM extraction SHALL be schema-constrained per entity type

The extraction worker SHALL invoke the LLM separately for each candidate entity (or chunk of related text), passing a **discriminated-union (`oneOf`) JSON schema** as the constraint: a small set of plausible entity-type branches (each with a `const` `entityType` discriminator and that type's fields) plus a `{"entityType":"none","reason":string}` decline branch. The union SHALL be passed through the existing grammar-constrained decoding path (`ChatResponseFormat.ForJsonSchema`); the model selects exactly one branch. The LLM output SHALL be validated against the selected branch before being added to the canonical JSON. Records that fail validation after a bounded retry budget SHALL be written to a `data/canonical/<book-slug>.errors.json` file for human review and SHALL NOT appear in the canonical JSON.

#### Scenario: Branch-conforming LLM output is added to canonical JSON

- **WHEN** the LLM returns JSON that validates against the selected union branch (a typed entity)
- **THEN** the record is added to the canonical JSON's `entities[]` array with the model-selected `entityType`

#### Scenario: Decline branch produces no typed entity

- **WHEN** the LLM selects the `{"entityType":"none"}` branch for a candidate
- **THEN** no typed entity is added to `entities[]`; the candidate is recorded as `Declined` (per the `extraction-disposition` capability)

#### Scenario: Schema-violating LLM output is recorded in errors file

- **WHEN** the LLM returns JSON that does not validate against any union branch after retries
- **THEN** the offending output and validation errors are appended to `data/canonical/<book-slug>.errors.json` and the canonical JSON is unaffected

### Requirement: LLM extraction produces correct entity type

The model SHALL determine each entity's `EntityType` by selecting the matching branch of the discriminated-union schema — `Class`, `Subclass`, `Spell`, `Monster`, `Feat`, `Item`, `MagicItem`, `Race`, `Subrace`, `Background`, `Rule`, `God`, `Condition`, `DiseasePoison`, `Weapon`, `Armor`, `Trap`, `VehicleMount` — based on the content it reads. The keyword classifier (`HeadingCategoryClassifier`) SHALL NOT determine the final type; it serves only as a prior that prunes the offered branches. When no offered branch fits, the model SHALL select the `none` (decline) branch rather than defaulting to `Class` or fabricating a type.

#### Scenario: Subclass correctly typed

- **WHEN** the LLM extracts an entity for "Circle of Spores" from Tasha's Cauldron of Everything
- **THEN** the extracted entity SHALL have `type: "Subclass"` not `type: "Class"`

#### Scenario: Rule entry correctly typed

- **WHEN** the LLM extracts an entity for "Transmuted Spell" (a metamagic option)
- **THEN** the extracted entity SHALL have `type: "Rule"` not `type: "Class"`

#### Scenario: Unknown content declines instead of defaulting to Class

- **WHEN** the LLM cannot fit a candidate to any offered branch
- **THEN** the LLM SHALL select the `none` (decline) branch, and the candidate SHALL be recorded as `Declined` rather than fabricated as a `Class`
