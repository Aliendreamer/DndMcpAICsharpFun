# programmatic-world-extraction Specification

## Purpose
TBD - created by archiving change programmatic-extraction-phase2c. Update Purpose after archive.
## Requirements
### Requirement: God parsing

The extractor SHALL produce a God entity for each printed deity table row and each printed deity section, with
alignment, domains, symbol and pantheon only as printed, and alignment and domains from closed sets.

#### Scenario: PHB deity row

- **WHEN** the PHB is extracted
- **THEN** `phb14.god.anubis` has pantheon "Egyptian", alignment ["L", "N"], domains including "Death" and symbol
  "Black jackal", and all checks pass

#### Scenario: Unknown domain

- **WHEN** a deity row's domains OCR as "Tempset"
- **THEN** a `domains-known` check fails naming "Tempset" and the god is NeedsReview

### Requirement: Rule parsing

The extractor SHALL produce a Rule entity for each named rule section in rules chapters, each "Variant:" or "Optional
Rule:" section and each downtime activity, with heading-bounded text and `ruleType` only when the print marks it.

#### Scenario: Variant rule

- **WHEN** the PHB is extracted
- **THEN** a Rule named "Customizing Ability Scores" exists with `ruleType` "V" and its text ends before the next
  section heading

### Requirement: World section parsing

The extractor SHALL produce Plane, Faction, Location and Lore entities from heading-bounded sections of setting and
lore chapters at each chapter's entry level, typed by the nearest printed enclosing heading, and SHALL record the
typing evidence in a passing `type-from-context` check (failing when context words conflict).

#### Scenario: Faction section

- **WHEN** GGR is extracted
- **THEN** each guild chapter's guild section is a Faction whose `type-from-context` names the enclosing heading

### Requirement: World self-checks

Each world entity SHALL carry checks feeding D4: `text-complete` (section ends at a heading of equal or greater level
or a chapter title), `type-from-context` (section types), `domains-known` and `alignment-known` (God).

#### Scenario: Run-on section

- **WHEN** a section's end cannot be found before end of chapter
- **THEN** `text-complete` fails naming the section and it is NeedsReview

