# monster-candidate-recovery Specification

## Purpose
TBD - created by archiving change mm-monster-recall. Update Purpose after archive.
## Requirements
### Requirement: 5etools-roster monster candidate recovery

For an official book (one with a `fivetoolsSourceKey`), candidate generation SHALL recover a section
as a candidate when its heading confidently matches **any** indexed 5etools entity, even if the
section's page classifies as a non-entity TOC category. Recovery is NOT restricted to the monster
roster: the recovered candidate SHALL carry **the matched entry's own entity type** as its primary
prior (a Race match yields a `Race` candidate, a Subclass match a `Subclass` candidate, and so on).

Matching SHALL prefer entries whose 5etools `source` equals the book's own source key, falling back to
the global index when no same-source entry matches. Preferring the book's own source is what prevents a
heading from being typed by another book's entry — the defect by which EGW's race and subrace pages
matched other books' bestiary entries and were emitted as `Monster` candidates.

Recovery reuses the existing `EntityNameMatcher`/`EntityNameIndex` at the established confidence bar
and only ADDS candidates — a non-match leaves prior behavior unchanged, so recovery cannot lower
precision. Cost is therefore bounded by the roster rather than by the number of skipped sections.

#### Scenario: Monster-name heading on a Rule page is recovered

- **WHEN** an official book's section heading (e.g. `ABOLETH`) would be skipped because its page's TOC
  category is not entity-eligible, and the heading confidently matches a 5etools monster name
- **THEN** the section is emitted as a `Monster` candidate carrying the matched canonical name, rather
  than being skipped

#### Scenario: A non-monster heading on a Rule page is recovered with its own type

- **WHEN** an official book's section heading is `ECHO KNIGHT`, `CHRONURGY MAGIC` or `GRAVITURGY MAGIC`
  on a page whose TOC category is `Rule`, and the heading confidently matches a 5etools subclass
- **THEN** the section is emitted as a `Subclass` candidate (not skipped, and not typed `Monster`)

#### Scenario: A same-source match wins over another book's entry of a different type

- **WHEN** the book is EGW and a skipped section's heading is `Orc`, which matches both an EGW `Race`
  entry and a bestiary `Monster` entry from another source
- **THEN** the EGW `Race` entry is preferred and the candidate is emitted as a `Race`, not a `Monster`

#### Scenario: Global fallback still applies when the book has no matching entry

- **WHEN** a skipped section's heading matches no entry attributed to the book's own source but does
  confidently match an entry from another source
- **THEN** that entry is used, so a reprint is still recovered rather than dropped

#### Scenario: Non-matching heading is not recovered

- **WHEN** a skipped section's heading does not confidently match any indexed 5etools entity
- **THEN** no candidate is recovered for it and the prior (structural) rules decide, so
  precision is unaffected

#### Scenario: Non-official book is unaffected by roster recovery

- **WHEN** a book has no `fivetoolsSourceKey`
- **THEN** 5etools-roster recovery does not run for it

### Requirement: Authoritative stat-block scanning

A detected stat block SHALL always be emitted as a candidate regardless of the TOC category of its
page, because an `Armor Class / Hit Points / Speed` structure is a definitive creature signal.

#### Scenario: Stat block on a Rule page still yields a candidate

- **WHEN** a stat block is detected on a page whose TOC category is `Rule`
- **THEN** it is emitted as a candidate rather than suppressed by the TOC category

### Requirement: Ungate scanning when TOC categorization has failed

When a book yields stat blocks but zero Monster TOC pages, candidate generation SHALL treat TOC
categorization as failed for that book and MUST NOT let the TOC-category gate suppress its sections;
the downstream extraction decline gate is relied upon to filter non-entities instead.

#### Scenario: Bestiary with no Monster TOC pages is not gated away

- **WHEN** a book produces stat blocks but its TOC map contains zero Monster-category pages
- **THEN** its sections are not dropped on TOC-category grounds, and non-monster sections are filtered
  by the extraction decline gate rather than by the TOC gate

### Requirement: 5etools monster recall check

The system SHALL provide a recall check that diffs a book's extracted canonical monsters against the
authoritative 5etools monster roster for that book's source, returning the precise set of missing and
extra monsters. The roster MUST be filtered by 5etools source (monsters are cross-source attributed),
and the check MUST report the grounded-vs-backfilled split.

#### Scenario: Missing monsters are reported against the roster

- **WHEN** the recall check runs for the Monster Manual
- **THEN** it returns the monsters present in the 5etools `MM`-source roster but absent from
  `mm14.json`, and the extra monsters present in the canonical but not the roster

