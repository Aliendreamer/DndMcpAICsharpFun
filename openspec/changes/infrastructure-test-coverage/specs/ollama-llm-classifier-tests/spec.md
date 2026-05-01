## ADDED Requirements

### Requirement: OllamaLlmClassifier returns categories from LLM response
The system SHALL parse a JSON array of category strings from the LLM streamed response.

#### Scenario: Happy path returns parsed categories
- **WHEN** the LLM returns a valid JSON array such as `["Spell", "Monster"]`
- **THEN** `ClassifyPageAsync` returns a list containing those strings

#### Scenario: Empty array response returns empty list
- **WHEN** the LLM returns `[]`
- **THEN** `ClassifyPageAsync` returns an empty list

#### Scenario: Invalid JSON response returns empty list
- **WHEN** the LLM returns non-JSON text
- **THEN** `ClassifyPageAsync` returns an empty list without throwing

#### Scenario: Null or empty string response returns empty list
- **WHEN** the LLM returns an empty string
- **THEN** `ClassifyPageAsync` returns an empty list without throwing

#### Scenario: Cancellation propagates
- **WHEN** the `CancellationToken` is already cancelled before the call
- **THEN** `ClassifyPageAsync` throws `OperationCanceledException`
