# model-eval-harness Specification

## Purpose
TBD - created by archiving change model-eval-harness. Update Purpose after archive.
## Requirements
### Requirement: Eval harness runs tool scenarios through the real MEAI stack

`Tools/ModelEval` SHALL evaluate tool-use scenarios by driving the same MEAI
`OllamaChatClient(...).UseFunctionInvocation()` pipeline the app uses, with the top chat tools
registered as real `AIFunction` schemas backed by STUBBED delegates returning canned results. It MUST
exercise the MEAI argument binder (not merely the model's raw emit), so a binding failure that a raw
`/api/chat` probe would miss is caught and scored as a bind failure.

#### Scenario: A binding failure is detected

- **WHEN** a scenario's model call emits tool args that MEAI cannot bind to the delegate (e.g. a missing
  required parameter)
- **THEN** that run is scored as bind-fail (not selection-pass), because the harness runs through the
  real `FunctionInvocation` binder

#### Scenario: Stubbed delegates isolate model behavior

- **WHEN** a scenario runs
- **THEN** the tool delegate returns a fixed canned (or empty) result with no retrieval/DB dependency,
  so the score reflects the model's tool behavior, not corpus state

### Requirement: Scorecard reports N-run pass-rates across four dimensions

For each scenario the harness SHALL run the model N times (default 5, configurable) and report
aggregate pass-rates for **selection** (the expected tool was invoked, or no tool for a negative
scenario), **binding** (args bound without a MEAI error), and **adherence** (final-text checks:
reports the stub's exact value, does not fabricate when the stub is empty, honors prose-not-list), plus
**latency** (p50/p95 wall-clock and time-to-first-tool-call). It SHALL print a per-scenario table and a
totals row.

#### Scenario: Negative scenario expects no tool

- **WHEN** a chit-chat scenario ("hi, how's it going") with `ExpectedTool = None` runs
- **THEN** selection passes only on runs where NO tool was invoked (catching over-triggering)

#### Scenario: Empty-result scenario scores fabrication

- **WHEN** a scenario's stub returns an empty result
- **THEN** the adherence check passes only if the final text declines/says-not-covered rather than
  inventing an answer

### Requirement: Model and think-mode are selectable per run

The harness SHALL accept the Ollama model name and the think-mode (on/off) as arguments, so the same
scenario set can be scored for `qwen3:8b` think-on vs think-off vs a same-size alternative by changing
one argument. The think-off path MUST actually suppress qwen3's thinking block (verified against
Ollama).

#### Scenario: Think-off is a distinct measured config

- **WHEN** the harness is run with think-off vs think-on for the same model
- **THEN** it produces two comparable scorecards, and think-off's runs do not emit a `<think>` block

### Requirement: The chat path think-mode follows the harness measurement

The production chat client's think-mode SHALL be set to whichever the harness measures as better, not
assumed. If chat-path `think:false` improves latency without regressing selection, binding, or
adherence, it MUST be applied (mirroring the extraction path); if think-off regresses quality, the chat
path MUST remain think-on and the finding MUST be recorded.

#### Scenario: Decision follows the measurement

- **WHEN** the think-on vs think-off scorecards are compared
- **THEN** the chat path's think-mode is set to whichever the data supports, not assumed

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

