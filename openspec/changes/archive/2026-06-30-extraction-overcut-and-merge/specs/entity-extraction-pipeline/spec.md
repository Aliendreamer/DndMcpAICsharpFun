## ADDED Requirements

### Requirement: Same-titled sections far apart yield separate candidates

The candidate scanner SHALL group blocks sharing a section title only when they are within a small page
window of each other; same-titled sections that are far apart (a name reused in different chapters) SHALL
become separate candidates, each keyed on its own page so each is categorized by its own chapter. A
header repeated on an adjacent continuation page MUST still merge into one candidate.

#### Scenario: A name reused across chapters is not merged
- **WHEN** "DARKVISION" appears as a section title at page 184 (an invocation) and again at page 230 (the spell), far beyond the page window
- **THEN** the scanner emits two candidates — one keyed at page 184, one at page 230 — not one merged candidate keyed at page 184

#### Scenario: A continuation-page repeat still merges
- **WHEN** the same section title appears on adjacent pages within the page window
- **THEN** the scanner merges them into one candidate
