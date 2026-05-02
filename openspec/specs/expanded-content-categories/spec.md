# expanded-content-categories

## Purpose

TBD — defines the six additional prose-only `ContentCategory` values (`God`, `Combat`, `Adventuring`, `Condition`, `Plane`, `Race`) and how the TOC classifier and entity extractor handle them.

## Requirements

### Requirement: Six new content categories
The system SHALL support six additional `ContentCategory` values: `God`, `Combat`, `Adventuring`, `Condition`, `Plane`, `Race`. All six are prose-only and extract a single `description` field.

#### Scenario: New categories accepted by entity extractor
- **WHEN** the TOC classifier assigns `Combat`, `Adventuring`, `Condition`, `God`, `Plane`, or `Race` to a page
- **THEN** the entity extractor SHALL use the matching TypeFields entry (`description (string)`) without falling back to the default

#### Scenario: TOC classifier maps chapter titles to new categories
- **WHEN** the TOC classifier receives a chapter title such as "Chapter 9: Combat", "Appendix A: Conditions", or "Chapter 2: Races"
- **THEN** it SHALL return the corresponding specific category (`Combat`, `Condition`, `Race`) rather than `Rule` or `null`

#### Scenario: Rule remains the catch-all
- **WHEN** a chapter title does not match any specific category
- **THEN** the TOC classifier SHALL return `Rule`
