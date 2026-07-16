## MODIFIED Requirements

### Requirement: Decline a non-matching official candidate of a gated type

The system SHALL decline a candidate, with reason `no_5etools_match` and without making an extraction
LLM call, when the book is official, the candidate does not match the 5etools index, the candidate has
no complete stat block, the candidate's PRIMARY prior type (the first, bookmark-derived entry of its
prior list) is in the gated set, **and the candidate carries no book-derived entity signature** (a
complete stat block, spell signature, item signature, or subclass-feature-progression signature). A
declined candidate SHALL NOT be emitted as an entity. When a non-matching official gated candidate
DOES carry an entity signature, the system SHALL NOT decline it: it SHALL fall through to content-first
extraction grounded by the book's own prose, its fields validated by the grounding cascade, and label
it `canon-unindexed`. The system SHALL also fall through to content-first extraction when the
candidate's primary prior type is ungated or its prior set is empty. (The gate uses the primary prior
because the scanner always appends a frequency floor — including the ungated Item — to every
candidate's prior list.)

#### Scenario: Chapter-body noise of a gated type is declined
- **WHEN** an official book yields a candidate "Ability Score Increase" with primary prior type {Race} (the scanner also appended the {Monster, Spell, Item, Class} floor), no 5etools match, no complete stat block, and no entity signature
- **THEN** it is declined with reason `no_5etools_match` and no extraction LLM call is made for it

#### Scenario: A non-matching official entity with a signature is admitted, not declined
- **WHEN** an official book yields a gated-prior candidate with no 5etools match but a book-derived entity signature (e.g. a subclass-feature progression)
- **THEN** it is NOT declined; it falls through to content-first extraction, is grounded by the cascade, and is labeled `canon-unindexed`

#### Scenario: A real gated entity is not declined
- **WHEN** an official book yields a candidate "Fireball" with prior type(s) {Spell} that matches the 5etools index
- **THEN** it is forced to its matched type and canonical name (the gate does not fire on a match)

#### Scenario: Ungated primary prior falls through
- **WHEN** an official-book candidate has primary prior type {Item} (ungated) and no 5etools match
- **THEN** it is NOT declined and falls through to content-first extraction

#### Scenario: Homebrew candidate of a gated type is not declined
- **WHEN** a homebrew book yields a candidate of prior type {Class} with no 5etools match
- **THEN** it is NOT declined and falls through to content-first extraction
