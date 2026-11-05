## ADDED Requirements

### Requirement: The full-chain eval MUST generate monster cases from canonical data

The harness SHALL generate monster cases (armor class, hit points, challenge rating) from canonical entities of type
`Monster` that are not flagged `needsReview`, and SHALL read `mm14` by default alongside `phb14`. Answer variants MUST be
unit-qualified, never a bare number.

#### Scenario: Monster case from a reviewed entity

- **WHEN** `mm14` contains a reviewed Monster with an armor class of 16
- **THEN** a generated monster case MUST expect an answer containing that armor class with its unit (e.g. "armor class
  16")

#### Scenario: Unreviewed monsters are excluded

- **WHEN** a Monster entity has `needsReview` true
- **THEN** the generator MUST NOT produce a case from it

### Requirement: The manual case set MUST cover rules, lore, NPC and not-found questions

`Tools/ModelEval/cases/manual.json` SHALL contain cases in the categories `rules`, `lore`, `npc` and `not-found`, and the
manual-case loader MUST accept them. A `not-found` case SHALL pass only when the reply says the information could not be
found.

#### Scenario: Not-found answer passes only with a could-not-find reply

- **WHEN** a not-found case asks about a spell that is not in the data and the reply invents a level for it
- **THEN** the run MUST NOT pass

### Requirement: The full-chain report MUST record per-run detail

Each case in a full-chain report SHALL record, per run, the names of the called tools and the reply text truncated to
600 characters. Reports written before this change MUST still load as baselines.

#### Scenario: Old report still loads as a baseline

- **WHEN** the baseline report has no per-run detail
- **THEN** the comparison MUST still run on shared case ids
