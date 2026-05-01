## Why

When the LLM returns unparseable JSON during entity extraction, the current fallback saves raw page text as a garbage `Rule` entity — polluting the extracted dataset with meaningless entries and causing downstream ingestion to embed nonsense. We need a retry mechanism and a clean skip-on-failure behaviour instead.

## What Changes

- Add `LlmExtractionRetries` (int, default 1) to `IngestionOptions` / `appsettings.json`
- Replace the garbage fallback in `OllamaLlmEntityExtractor` with a retry loop (same prompt, up to `LlmExtractionRetries` retries)
- After all retries exhausted: log warning, return `[]` — nothing saved for that page/type
- Remove the `page_{n}_raw` Rule entity creation entirely

## Capabilities

### New Capabilities
- none

### Modified Capabilities
- `llm-extraction`: error handling for invalid JSON in Pass 2 now retries before skipping, instead of saving garbage fallback data

## Impact

- `OllamaLlmEntityExtractor` — retry loop replaces fallback
- `IngestionOptions` — new `LlmExtractionRetries` property
- `appsettings.json` — new `Ingestion:LlmExtractionRetries` key
- `openspec/specs/llm-extraction/spec.md` — error handling row updated
