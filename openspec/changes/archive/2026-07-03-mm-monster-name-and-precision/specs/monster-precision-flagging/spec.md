## ADDED Requirements

### Requirement: Categorize recall-check extra monsters

The monster recall check SHALL split its `extra` set (canonical monsters absent from the book's
source-filtered 5etools roster) into `extraOtherSource` (the name matches a Monster in the full 5etools
index under a different source) and `extraUnknown` (the name matches no 5etools monster at all). Both MUST
be reported so cross-printed monsters are distinguished from likely false positives.

#### Scenario: Cross-source vs unknown extra are distinguished

- **WHEN** the recall check runs for the Monster Manual and the canonical contains `Orcus` (a Monster in
  5etools under MPMM/MTF, not MM) and `Roa` (not in 5etools at all)
- **THEN** `Orcus` is reported under `extraOtherSource` and `Roa` under `extraUnknown`

### Requirement: Flag unknown extra monsters for review

The system SHALL provide an operation that marks each `extraUnknown` monster in a book's canonical
`NeedsReview = true`, rewriting the canonical with the flag set. It MUST NOT delete any entity and MUST NOT
flag `extraOtherSource` monsters.

#### Scenario: Unknown-extra monsters are flagged, not deleted

- **WHEN** the precision-flag operation runs for the Monster Manual
- **THEN** each `extraUnknown` monster (e.g. `Lord Soth`, `Roa`) has `NeedsReview` set true in the canonical,
  no entity is removed, and `extraOtherSource` monsters are untouched
