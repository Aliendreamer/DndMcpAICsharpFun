## ADDED Requirements

### Requirement: Extraction think mode is configurable

The entity-extraction pipeline SHALL expose a configurable qwen3 think mode. An `EntityExtractionOptions.Think` option SHALL flow to the Ollama chat request's `think` field. When the option is unset (null), the request SHALL NOT set `think`, preserving the model's default behaviour. When set to `false`, the request SHALL disable thinking; when `true`, it SHALL enable it.

#### Scenario: Default is unchanged behaviour

- **WHEN** the `Think` option is not configured
- **THEN** the Ollama extraction request does not include a `think` field and the model behaves as it did before this change

#### Scenario: Thinking can be disabled

- **WHEN** `Think` is set to `false`
- **THEN** the Ollama extraction request sets `think` to `false` so the model does not spend output budget on a reasoning block

### Requirement: Reasoning never leaks into extracted content

The extraction client SHALL strip any well-formed `<think>…</think>` block from a model response before the response is parsed into entity fields, regardless of think mode.

#### Scenario: Inline think block is removed before parsing

- **WHEN** a model response contains a `<think>…</think>` block preceding the tool output
- **THEN** the block is removed and only the entity content is parsed

### Requirement: Per-candidate extraction outcome is measurable

The pipeline SHALL record, per candidate, the think mode, the wall-clock duration, the selected entity type, and the outcome (extracted, empty response, or declined), so that two runs over the same sample can be compared for speed, empty-response rate, and classification differences.

#### Scenario: A run is comparable

- **WHEN** the same candidate sample is extracted once with thinking on and once with thinking off
- **THEN** each run yields per-candidate records of duration, type, and outcome sufficient to compute total time, empty-response counts, and type-classification differences between the two runs
