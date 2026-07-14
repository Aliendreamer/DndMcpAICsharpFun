## ADDED Requirements

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
