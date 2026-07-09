# entity-grounding-cascade Specification

## Purpose
TBD - created by archiving change entity-grounding-cascade. Update Purpose after archive.
## Requirements
### Requirement: Shared grounding cascade returns a graded verdict

The system SHALL provide a shared `GroundingCascade` that grades one entity against its source
prose and returns a `GroundingVerdict` with a `Status` of `Grounded`, `Ungrounded`, or `Uncertain`,
the `DecidedByTier` (0, 1, or 2), and a `Score`. The verdict combination SHALL be a pure function of
the Tier 0 result, the Tier 1 similarity relative to the configured floor, and the Tier 2 judge
result, so it is testable without I/O. The same cascade SHALL be used by both extraction-time
grading and the backlog re-grounding pass.

#### Scenario: Tier 0 field match yields Grounded

- **WHEN** at least one significant emitted field value OCR-fuzzy-matches the source prose
- **THEN** the verdict is `Grounded` with `DecidedByTier = 0` and Tiers 1–2 are NOT run

#### Scenario: Verdict combination is pure and deterministic

- **WHEN** the cascade is graded twice with the same (Tier 0 bool, Tier 1 score, Tier 2 result) inputs
- **THEN** it returns the same `Status` and `DecidedByTier` both times

### Requirement: Tier 1 embedding grounding is scoped and escalation-only

Tier 1 SHALL embed the entity's text and query `dnd_blocks` filtered to the entity's own
`SourceBook` and to pages within a configured window of the entity's `Page`. When the top
similarity is below the configured floor, Tier 1 SHALL return `Ungrounded` (no supporting prose in
the entity's neighbourhood). When the top similarity is at or above the floor but Tier 0 did not
confirm, Tier 1 SHALL escalate to Tier 2. Tier 1 SHALL NEVER return `Grounded` on topical
similarity alone.

#### Scenario: No nearby supporting prose rejects at Tier 1

- **WHEN** the top `dnd_blocks` similarity within the entity's own book and page window is below the floor
- **THEN** the verdict is `Ungrounded` with `DecidedByTier = 1`

#### Scenario: Topical similarity escalates rather than grounds

- **WHEN** the top similarity is at or above the floor but no field was Tier-0 confirmed
- **THEN** Tier 1 SHALL NOT return `Grounded`; the entity SHALL be escalated to the Tier 2 judge

#### Scenario: Tier 1 is scoped to the entity's own book and page window

- **WHEN** Tier 1 grounds an entity
- **THEN** the `dnd_blocks` query SHALL be restricted to `SourceBook == entity.SourceBook` and pages within the configured window of `entity.Page`

### Requirement: Tier 2 judge is opt-in and asks about field support

Tier 2 SHALL invoke an LLM judge (behind an injectable interface reusing the qwen3/Ollama client)
that is asked specifically whether the entity's emitted fields are supported by the source prose,
returning `Grounded` or `Ungrounded`. The judge SHALL be invoked only when enabled and only for the
residual the cheaper tiers did not resolve. When the judge is disabled or cannot decide, the verdict
SHALL be `Uncertain`.

#### Scenario: Judge runs only on the escalated residual

- **WHEN** Tier 0 confirms or Tier 1 rejects an entity
- **THEN** the Tier 2 judge SHALL NOT be invoked for that entity

#### Scenario: Judge disabled leaves the residual uncertain

- **WHEN** an entity escalates past Tier 1 but the judge is not enabled
- **THEN** the verdict is `Uncertain` and no automatic promotion or flagging occurs

#### Scenario: Judge confirms fabrication

- **WHEN** the judge determines the emitted fields are not supported by the source prose
- **THEN** the verdict is `Ungrounded` with `DecidedByTier = 2`

### Requirement: Verdict drives promotion, flagging, or no-op with a name gate

The system SHALL map a `GroundingVerdict` to an action on a `NeedsReview` entity: `Grounded` SHALL
clear `NeedsReview` (promote to `Accepted`); `Ungrounded` SHALL set disposition `Ungrounded`;
`Uncertain` SHALL leave the entity unchanged (`NeedsReview`). Promotion SHALL be name-gated: an
entity whose review reason is `ocr-artifact` (`ExtractionNeedsReview.HasOcrArtifacts(name)` is true)
SHALL NOT be promoted even when `Grounded` — it SHALL remain `NeedsReview`. An `Ungrounded`
disposition SHALL only be set when the Tier 2 judge ran (or Tier 1 floor-rejected with the judge
enabled); a fabrication SHALL NOT be auto-flagged on Tier 1 alone without the judge.

#### Scenario: Grounded entity with a clean name is promoted

- **WHEN** a `low-confidence` entity grades `Grounded` and its name has no OCR artifact
- **THEN** its `NeedsReview` is cleared and its disposition becomes `Accepted`

#### Scenario: Grounded content with a garbled name stays flagged

- **WHEN** an `ocr-artifact` entity grades `Grounded` but `HasOcrArtifacts(name)` is still true
- **THEN** the entity remains `NeedsReview` (grounding does not clear a name problem)

#### Scenario: Uncertain leaves the entity flagged

- **WHEN** an entity grades `Uncertain`
- **THEN** its `NeedsReview` and disposition are unchanged

#### Scenario: Fabrication is not auto-flagged without the judge

- **WHEN** Tier 1 floor-rejects an entity but the judge is disabled
- **THEN** the entity is NOT set to `Ungrounded`; it stays `NeedsReview`

### Requirement: Per-book backlog re-grounding endpoint

The system SHALL expose `POST /admin/books/{id}/reground-entities`, guarded by the admin API key,
that runs the cascade over the book's `NeedsReview` entities and applies the verdict→action policy.
A `judge` query flag (default false) SHALL opt into Tier 2; without it only Tiers 0–1 run. For each
changed entity the endpoint SHALL write the canonical file in place and re-index only that entity
into Qdrant, leaving other entities untouched, and SHALL NOT delete any canonical file. The run
SHALL be checkpointed to a `<slug>.reground.progress.json` sidecar (deleted on success, resumed on
retry) and SHALL return a summary `{ scanned, promoted, markedUngrounded, stillFlagged,
tier2Invoked }`.

#### Scenario: Fast pass without the judge

- **WHEN** `POST /admin/books/{id}/reground-entities` is called without `judge=true`
- **THEN** only Tiers 0–1 run, no LLM judge is invoked, and the summary reports `tier2Invoked = 0`

#### Scenario: Promoted and flagged entities are written and re-indexed

- **WHEN** the pass promotes one entity and marks another `Ungrounded`
- **THEN** both changes are written to the canonical file, each changed entity is re-indexed into Qdrant, and no other entity's Qdrant point is modified

#### Scenario: Interrupted run resumes from checkpoint

- **WHEN** a reground run is interrupted after writing a checkpoint and is retried
- **THEN** it resumes from the checkpoint rather than reprocessing completed entities

#### Scenario: Canonical files are never deleted

- **WHEN** the reground pass marks entities `Ungrounded`
- **THEN** no file under `books/canonical/` is deleted

