## 1. Domain

- [x] 1.1 Add `God`, `Combat`, `Adventuring`, `Condition`, `Plane`, `Race` to `ContentCategory` enum in `Domain/ContentCategory.cs`

## 2. Entity Extractor

- [x] 2.1 Add entries for all six new categories to `TypeFields` in `OllamaLlmEntityExtractor.cs` (all `description (string)`)
- [x] 2.2 Update `OllamaLlmEntityExtractor` system prompt to list all valid categories including the six new ones
- [x] 2.3 Update `StripFences` to unwrap single-key JSON objects (e.g. `{"entities":[...]}`) before parsing — already implemented, verify it covers all cases

## 3. TOC Classifier

- [x] 3.1 Update `OllamaTocCategoryClassifier` system prompt valid categories list to include the six new values
- [x] 3.2 Add concrete mapping examples to the TOC prompt: `"Combat" → Combat`, `"Conditions" → Condition`, `"Races" → Race`, `"Gods"/"Deities" → God`, `"Planes" → Plane`, `"Adventuring" → Adventuring`

## 4. Bookmark Reader

- [x] 4.1 Verify `PdfPigBookmarkReader` returns depth 0 + depth 1 only — already implemented, confirm tests pass

## 5. Tests

- [x] 5.1 Verify all three `PdfPigBookmarkReaderTests` pass with current implementation
- [x] 5.2 Update or add tests for `OllamaLlmEntityExtractor` covering the JSON unwrap behaviour
- [x] 5.3 Run full test suite and confirm no regressions (120 passed)

## 6. Build & Deploy

- [x] 6.1 Build project (`dotnet build`) with no errors
- [ ] 6.2 Rebuild Docker image and redeploy (`docker compose up -d --build app`)
- [ ] 6.3 Re-trigger extraction and JSON ingestion for any previously ingested books
