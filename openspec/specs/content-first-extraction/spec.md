# content-first-extraction Specification

## Purpose
TBD - created by archiving change extraction-honesty. Update Purpose after archive.
## Requirements
### Requirement: Model selects its entity type or declines via a discriminated-union schema

The extractor SHALL constrain the LLM with a discriminated-union (`oneOf`) JSON schema whose branches are candidate entity types (each with a `const` `entityType` discriminator and that type's fields) plus a decline branch `{"entityType":"none","reason":string}`. The schema SHALL be passed through the existing grammar-constrained decoding path (`ChatResponseFormat.ForJsonSchema`). The model — not the keyword classifier — SHALL determine the final entity type by selecting a branch, and SHALL be able to select `none` rather than being forced to populate a pre-chosen type.

#### Scenario: Model picks the correct type branch

- **WHEN** a candidate's source text is a spell and the union offers a `Spell` branch
- **THEN** the response is valid JSON on the `Spell` branch and the entity's type is `Spell`

#### Scenario: Model declines non-entity content instead of fabricating

- **WHEN** a candidate's source text is not a discrete game entity (a heading, index, or table fragment)
- **THEN** the model selects the `{"entityType":"none"}` branch and no typed entity is fabricated

#### Scenario: Previously mis-typed content is re-typed, not forced to Monster

- **WHEN** a candidate's source text is racial/ancestry content that the keyword classifier would have frozen as `Monster`
- **THEN** the resulting `entityType` is a race-appropriate branch or `none`, and SHALL NOT be `Monster`

### Requirement: The keyword classifier is a union-pruning prior, not the type authority

`HeadingCategoryClassifier` SHALL no longer determine the final entity type. It SHALL instead produce a ranked set used to prune the union to a small set of plausible branches (a frequency-floor of common types ∪ the guess and its empirical confusion set), and the decline branch `none` SHALL always be included regardless of the prior.

#### Scenario: Union is pruned by the prior

- **WHEN** the classifier's prior for a candidate yields a small ranked set
- **THEN** the union schema offered to the model contains those branches plus `none`, not all entity types

#### Scenario: A wrong prior degrades to a decline, never a fabrication

- **WHEN** the prior omits the correct type for a candidate (a mis-prune)
- **THEN** because `none` is always offered, the model can decline rather than being forced into an offered wrong type, and SHALL NOT fabricate an entity of an offered type

