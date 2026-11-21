# Spec Delta

## Purpose

A page-verified answer key for the programmatic extractor: every entity a reviewer checked against its book page, with
its OCR'd pages, so extractor changes are measured against what the book actually prints before they ship.

## ADDED Requirements

### Requirement: Regression set from reviewed entities

The extractor tests SHALL include a regression set holding every programmatic entity that has an approval or a field
override in `books/canonical/<slug>.overrides.json`. Each entry SHALL hold the entity id, type, page numbers, the OCR'd
lines of those pages, and the expected text and fields: the override value where one exists, else the approved extraction.

#### Scenario: Corrected entity in the set

- **WHEN** `phb14.rule.concentration` has a `canonicalText` override that removed the "Schools of Magic" sidebar
- **THEN** its regression entry expects the overridden text, without the sidebar

#### Scenario: Approved-as-is entity in the set

- **WHEN** an entity has an approval and no override
- **THEN** its regression entry expects its extracted text and fields unchanged

#### Scenario: Missing pages skipped

- **WHEN** an entity's pages are absent from the OCR cache used to build the set
- **THEN** the entity is left out and the builder names it

### Requirement: Builder is a dev tool

The regression set SHALL be built by a dev script from the overrides files, canonical files and the OCR cache, and
committed as test data. The set is append-only: an entry's expected values stay frozen once added. The script MUST NOT
run in CI or in the extractor service, and MUST NOT read 5etools data.

#### Scenario: Rebuild after a review

- **WHEN** a reviewer adds overrides and the builder is re-run
- **THEN** the committed set gains the new entities and keeps the existing ones unchanged

#### Scenario: Retired override stays in the set

- **WHEN** an override retired because the extractor now reproduces it, and the builder is re-run
- **THEN** that entity's entry and its expected values are still in the set

### Requirement: Regression scoring

A regression run SHALL extract every set entry from its OCR'd pages and report, per entry, token-F1 of the text against
the expected text and whether every overridden field now matches its expected value (whitespace, line breaks and
end-of-line hyphenation ignored). It SHALL print totals: matches, entries improved, entries worse. Entities not in the
set are ignored.

#### Scenario: Fixed boundary scores an exact match

- **WHEN** the extractor no longer glues the sidebar into Concentration
- **THEN** the Concentration entry reports a match for `canonicalText`

#### Scenario: Totals printed

- **WHEN** the regression run finishes
- **THEN** it prints the counts of matches, improved entries and worse entries

### Requirement: Regression gate

The extractor test suite SHALL fail when an entry that matched its expected values before a change no longer does, or
when any entry's token-F1 against its expected text drops by more than 0.005.

#### Scenario: Fix breaks another book

- **WHEN** a column-order change makes a previously exact Monster Manual entry lose a trait
- **THEN** the test suite fails naming that entity

#### Scenario: Improvement passes

- **WHEN** a change turns a non-matching entry into a match and no entry gets worse
- **THEN** the test suite passes
