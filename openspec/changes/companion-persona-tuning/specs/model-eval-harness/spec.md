## ADDED Requirements

### Requirement: The harness MUST support loading the chat persona from a file

The `ModelEval` console SHALL accept a `--persona <path>` argument and, when supplied, use that
file's text as the system persona for every scenario run; when omitted it SHALL fall back to the
built-in default persona string, so existing invocations are unchanged. This lets the rig measure a
real `companion.md` persona (or an edit variant on a host copy) without altering the encrypted file.

#### Scenario: Persona file overrides the default

- **WHEN** the console is run with `--persona <path>` pointing at a readable persona file
- **THEN** every scenario's system message MUST be that file's contents, not the built-in default

#### Scenario: Default persona when the flag is absent

- **WHEN** the console is run without `--persona`
- **THEN** it MUST use the built-in default persona string exactly as before (backward compatible)

### Requirement: The harness MUST score persona-adherence symptoms, not only tool selection

The scorecard SHALL support per-scenario adherence checks that inspect the model's FINAL composed
prose (the text produced after a tool result is returned), so persona symptoms are measurable. At
minimum it MUST cover: number-label correctness (the prose names a tool-result number by its true
field meaning and does not relabel it) and prose-not-list (the answer is flowing prose, not a
numbered or bulleted list) — in addition to the existing selection/binding scoring.

#### Scenario: Number-label check fails on a relabeled value

- **WHEN** a crafting scenario's tool returns a materials cost and the model's prose calls that value
  a "market value" (or otherwise mislabels it)
- **THEN** that run's number-label adherence check MUST record a failure

#### Scenario: Prose-not-list check fails on a bulleted answer

- **WHEN** a scenario's final prose contains a numbered (`1.`/`2.`) or bulleted list beyond the
  allowed threshold
- **THEN** that run's prose-not-list adherence check MUST record a failure

#### Scenario: Symptom checks are reported per scenario across N runs

- **WHEN** the harness runs a scenario N times
- **THEN** the scorecard MUST report the symptom checks as an N-run tally alongside the existing
  selection/binding/adherence columns, so a before/after persona comparison is possible
