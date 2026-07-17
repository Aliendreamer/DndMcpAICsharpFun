## MODIFIED Requirements

### Requirement: Decline a non-matching official candidate of a gated type

The system SHALL decline a candidate, with reason `no_5etools_match` and without making an extraction
LLM call, when the candidate does not match the 5etools index, the candidate's PRIMARY prior type (the
first, bookmark-derived entry of its prior list) is in the gated set, **and the candidate fails the
`IsRealEntity` predicate** (no structural signature — stat block / magic item /
subclass-feature-progression). A
declined candidate SHALL NOT be emitted as an entity. When a non-matching gated candidate DOES satisfy
`IsRealEntity`, the system SHALL NOT decline it: it SHALL fall through to content-first extraction
grounded by the book's own prose, its fields validated by the grounding cascade (an official such
entity is labeled `canon-unindexed`). This predicate gate SHALL apply to **both official and keyless
books** — previously only official books were gated and keyless books extracted every gated candidate;
now a keyless candidate that fails `IsRealEntity` is likewise declined. The system SHALL also fall
through to content-first extraction when the candidate's primary prior type is ungated or its prior set
is empty. (The gate uses the primary prior because the scanner always appends a frequency floor —
including the ungated Item — to every candidate's prior list.)

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

#### Scenario: A real homebrew/keyless entity falls through; keyless noise is declined
- **WHEN** a keyless (homebrew) book yields a gated-prior candidate with no 5etools match that satisfies `IsRealEntity`
- **THEN** it is NOT declined and falls through to content-first extraction; a keyless gated-prior candidate that FAILS `IsRealEntity` (table/TOC/fragment noise) is declined instead of extracted
