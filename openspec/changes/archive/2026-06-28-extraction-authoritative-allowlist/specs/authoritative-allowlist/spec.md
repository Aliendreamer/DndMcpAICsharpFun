## ADDED Requirements

### Requirement: Official-book determination

The system SHALL treat a book as official when its ingestion record carries a non-empty
`FivetoolsSourceKey`, and SHALL treat a book with no `FivetoolsSourceKey` as homebrew. The
authoritative-allowlist gate SHALL apply only to official books.

#### Scenario: Book with a source key is official
- **WHEN** a book's ingestion record has `FivetoolsSourceKey = "PHB"`
- **THEN** the book is official and the allowlist gate is active for it

#### Scenario: Book without a source key is homebrew
- **WHEN** a book's ingestion record has no `FivetoolsSourceKey`
- **THEN** the book is homebrew and the allowlist gate never fires (content-first throughout)

### Requirement: Gated entity types

The system SHALL define the gated set as exactly Spell, Monster, Class, Race, Background, Feat,
Condition, and God — the types for which the local 5etools mirror is a complete authoritative list.
Item, MagicItem, and Plane SHALL NOT be gated and SHALL retain content-first behavior unchanged.

#### Scenario: A gated type is subject to the allowlist
- **WHEN** the gated set is consulted for `Class`
- **THEN** `Class` is gated

#### Scenario: An ungated type is not subject to the allowlist
- **WHEN** the gated set is consulted for `Item`, `MagicItem`, or `Plane`
- **THEN** the type is not gated and the allowlist gate does not apply to it

### Requirement: Decline a non-matching official candidate of a gated type

The system SHALL decline a candidate, with reason `no_5etools_match` and without making an extraction
LLM call, when the book is official, the candidate does not match the 5etools index, the candidate has
no complete stat block, and the candidate's PRIMARY prior type (the first, bookmark-derived entry of
its prior list) is in the gated set; a declined candidate SHALL NOT be emitted as an entity. The system
SHALL instead fall through to content-first extraction when the candidate's primary prior type is
ungated or its prior set is empty. (The gate uses the primary prior because the scanner always appends
a frequency floor — including the ungated Item — to every candidate's prior list.)

#### Scenario: Chapter-body noise of a gated type is declined
- **WHEN** an official book yields a candidate "Ability Score Increase" with primary prior type {Race} (the scanner also appended the {Monster, Spell, Item, Class} floor), no 5etools match, and no complete stat block
- **THEN** it is declined with reason `no_5etools_match` and no extraction LLM call is made for it

#### Scenario: A real gated entity is not declined
- **WHEN** an official book yields a candidate "Fireball" with prior type(s) {Spell} that matches the 5etools index
- **THEN** it is forced to its matched type and canonical name (the gate does not fire on a match)

#### Scenario: Ungated primary prior falls through
- **WHEN** an official-book candidate has primary prior type {Item} (ungated) and no 5etools match
- **THEN** it is NOT declined and falls through to content-first extraction

#### Scenario: Homebrew candidate of a gated type is not declined
- **WHEN** a homebrew book yields a candidate of prior type {Class} with no 5etools match
- **THEN** it is NOT declined and falls through to content-first extraction

### Requirement: Declined records output file

The system SHALL write declined candidates for a book to a sibling file
`books/canonical/<book-slug>.declined.json` as a list of records `{ id, name, type, reason }`. The
declined records SHALL NOT appear in the canonical `entities`, and the file SHALL be separate from
`<book-slug>.errors.json` so that the `errorsOnly` re-extraction does not retry declined candidates.

#### Scenario: Declines are written to the sibling file
- **WHEN** extraction of an official book declines one or more candidates
- **THEN** each declined candidate is appended to `books/canonical/<book-slug>.declined.json` as `{ id, name, type, reason: "no_5etools_match" }` and none appear in the canonical `entities`

#### Scenario: errorsOnly retry ignores declined records
- **WHEN** an `errorsOnly` re-extraction runs for a book that has a `<book-slug>.declined.json`
- **THEN** the declined candidates are not retried (only `errors.json` entries are)
