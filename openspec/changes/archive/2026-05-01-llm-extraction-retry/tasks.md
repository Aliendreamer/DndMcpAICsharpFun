## 1. Configuration

- [x] 1.1 Add `LlmExtractionRetries` property (int, default 1) to `Infrastructure/Sqlite/IngestionOptions.cs`
- [x] 1.2 Add `"LlmExtractionRetries": 1` under the `Ingestion` section in `appsettings.json`

## 2. Retry Loop in OllamaLlmEntityExtractor

- [x] 2.1 Inject `IOptions<IngestionOptions>` into `OllamaLlmEntityExtractor` constructor
- [x] 2.2 Replace the single try/catch block with a `for` loop running `LlmExtractionRetries + 1` times — each iteration makes the full LLM call and attempts JSON parse
- [x] 2.3 On parse success: return results immediately
- [x] 2.4 On `JsonException`/`InvalidOperationException` with retries remaining: log debug message "Retrying extraction for {EntityType} page {Page} (attempt {Attempt}/{Max})" and continue loop
- [x] 2.5 On `JsonException`/`InvalidOperationException` with no retries remaining: log Warning and return `[]`
- [x] 2.6 Delete the garbage fallback entity creation (`page_{n}_raw` Rule entity)

## 3. Tests

- [x] 3.1 Unit test: valid JSON on first attempt → returns entities, no retry
- [x] 3.2 Unit test: invalid JSON on first attempt, valid on second → returns entities from second attempt
- [x] 3.3 Unit test: all attempts return invalid JSON → returns `[]`, Warning logged, no garbage entity
- [x] 3.4 Unit test: `LlmExtractionRetries: 0` + invalid JSON → single attempt only, returns `[]`
