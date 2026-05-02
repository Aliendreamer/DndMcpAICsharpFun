## Why

The current `ContentCategory` enum uses `Rule` as a catch-all for all non-entity prose content, which produces coarse retrieval results and causes the TOC classifier to return `null` for recognizable chapter types like Combat or Conditions. Adding specific categories improves retrieval precision and makes the TOC classifier more reliable.

## What Changes

- Add six new values to `ContentCategory`: `God`, `Combat`, `Adventuring`, `Condition`, `Plane`, `Race`
- Add corresponding entries to `TypeFields` in `OllamaLlmEntityExtractor` (all prose-only: `description (string)`)
- Update `OllamaTocCategoryClassifier` system prompt with the new valid categories and mapping examples
- Update `OllamaLlmEntityExtractor` system prompt to use the expanded category list
- Fix `PdfPigBookmarkReader` to return only root + depth-1 nodes instead of all flattened descendants
- Fix `OllamaLlmEntityExtractor.StripFences` to unwrap single-key JSON objects (e.g. `{"entities":[...]}`) before parsing
- Update affected unit tests

## Capabilities

### New Capabilities

- `expanded-content-categories`: Six new D&D content category values covering Combat, Adventuring, Conditions, Gods, Planes of Existence, and Races for more precise chunk tagging and retrieval

### Modified Capabilities

- `llm-extraction`: TOC classifier and entity extractor prompts updated to reflect the expanded category list; bookmark reader now returns 2-level depth; extractor JSON parser handles wrapped array responses
- `dm-content-categories`: `ContentCategory` enum extended with five new values

## Impact

- `Domain/ContentCategory.cs` — enum values added
- `Features/Ingestion/Extraction/OllamaLlmEntityExtractor.cs` — TypeFields + StripFences + prompt
- `Features/Ingestion/Extraction/OllamaTocCategoryClassifier.cs` — system prompt
- `Features/Ingestion/Pdf/PdfPigBookmarkReader.cs` — depth limit (already implemented)
- `DndMcpAICsharpFun.Tests/` — bookmark reader test + any category-related tests
- No API contract changes; no database migration needed (category stored as string)
