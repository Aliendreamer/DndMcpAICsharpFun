## MODIFIED Requirements

### Requirement: Pass 2 invalid JSON triggers retry then skip
The system SHALL retry the LLM extraction call up to `IngestionOptions.LlmExtractionRetries` times when the response cannot be parsed as valid JSON, and SHALL return an empty result (no entities saved) after all retries are exhausted.

#### Scenario: First attempt fails, retry succeeds
- **WHEN** the LLM returns unparseable JSON on the first attempt and valid JSON on the retry
- **THEN** the system returns the entities from the successful attempt and logs a debug-level retry message

#### Scenario: All retries exhausted
- **WHEN** all `LlmExtractionRetries + 1` attempts return unparseable JSON
- **THEN** the system logs a Warning and returns `[]` — no entity is saved for that page/type combination

#### Scenario: Zero retries configured
- **WHEN** `IngestionOptions.LlmExtractionRetries` is 0 and the single attempt returns invalid JSON
- **THEN** the system logs a Warning and returns `[]` immediately without retrying

#### Scenario: Valid JSON on first attempt
- **WHEN** the LLM returns valid JSON on the first attempt
- **THEN** the system returns the parsed entities without any retry

### Requirement: LLM extraction retry count is configurable
The system SHALL read the maximum retry count from `IngestionOptions.LlmExtractionRetries` with a default value of 1.

#### Scenario: Default retry count
- **WHEN** `Ingestion:LlmExtractionRetries` is not set in configuration
- **THEN** the system attempts the LLM call at most twice (1 original + 1 retry)

#### Scenario: Custom retry count via configuration
- **WHEN** `Ingestion:LlmExtractionRetries` is set to N in `appsettings.json`
- **THEN** the system attempts the LLM call at most N+1 times before returning `[]`

## REMOVED Requirements

### Requirement: Pass 2 invalid JSON saves raw page as Rule fallback
**Reason:** Raw page text saved as a `Rule` entity produces garbage data that pollutes Qdrant with semantically meaningless entries. The retry-then-skip behaviour is a safer and cleaner alternative.
**Migration:** No migration needed — the fallback entities were never valid D&D content and should not have been ingested. Re-extract affected books to remove them.
