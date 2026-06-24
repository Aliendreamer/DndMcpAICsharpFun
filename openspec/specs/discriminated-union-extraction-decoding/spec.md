# discriminated-union-extraction-decoding Specification

## Purpose
TBD - created by archiving change oneof-decoding-spike. Update Purpose after archive.
## Requirements
### Requirement: Union schema constrains output to one branch

The spike harness SHALL issue an extraction request whose `ChatResponseFormat.ForJsonSchema` input is a discriminated-union (`oneOf`) schema of at least three entity-type branches plus a `{"entityType":"none"}` decline branch, and SHALL verify that the model's response is valid JSON conforming to exactly one branch (a populated `entityType` discriminator plus that branch's fields).

#### Scenario: Constrained-valid union output

- **WHEN** the harness sends a candidate text with the union schema as the response format
- **THEN** the returned text parses as JSON, contains a single `entityType` discriminator value drawn from the schema's allowed set, and validates against that branch's sub-schema (no fields from other branches, no free-form prose)

#### Scenario: Decoding path does not error on a large union

- **WHEN** the union schema contains all entity-type branches plus the decline branch
- **THEN** the Ollama → llama.cpp structured-output path accepts the schema and returns a response without a grammar/parse error (or, if it errors, the error is captured verbatim in the findings)

### Requirement: Correct branch selection on known cases

The harness SHALL run the union request against known-labelled candidate texts — including the Draconic Ancestry / Dragonborn prose that the current pipeline mis-types as `Monster` — and SHALL record whether `qwen3:8b` selects the correct branch.

#### Scenario: Known entity routes to its branch

- **WHEN** the harness sends prose for a clearly-typed entity (e.g. a spell stat block)
- **THEN** the recorded `entityType` is that correct type, not a different branch

#### Scenario: Previously mis-typed content is re-typed or declined

- **WHEN** the harness sends the Draconic Ancestry / Dragonborn racial prose
- **THEN** the recorded `entityType` is a race-appropriate branch or `none` — and specifically NOT `Monster` (the failure the C2 fix targets)

### Requirement: Decline branch is usable on non-entity prose

The harness SHALL send at least one non-entity passage (a heading, table-of-contents line, or narrative paragraph) and SHALL record whether the model uses the `{"entityType":"none"}` decline branch rather than fabricating a typed entity.

#### Scenario: Non-entity prose declines

- **WHEN** the harness sends a non-entity passage
- **THEN** the recorded `entityType` is `none` (or the result is recorded as a decline-branch failure if the model fabricated a type instead)

### Requirement: Recorded go/no-go decision

The spike SHALL produce a `findings.md` capturing the observed results and a clear decision: **C2 confirmed** (union decoding works and branch selection is acceptable), **C2 conditional** (works but needs the classifier-as-prior pruning to be viable), or **C2 rejected** (fall back to C1 native tool-calling or a two-pass router), with the evidence that drove it.

#### Scenario: Decision is recorded and traceable

- **WHEN** the spike completes its runs
- **THEN** `findings.md` exists, states one of {C2 confirmed, C2 conditional, C2 rejected}, and references the per-case observations (branch chosen, validity, decline behaviour) that justify it

