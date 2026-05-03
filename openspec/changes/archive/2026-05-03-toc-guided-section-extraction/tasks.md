## 1. Domain & Categories

- [ ] 1.1 Add `Trait` and `Lore` to `ContentCategory` enum
- [ ] 1.2 Create `TocSectionEntry` record (`Title`, `Category`, `StartPage`, `EndPage`)
- [ ] 1.3 Enhance `TocCategoryMap` with `GetEntry(int page)` returning `TocSectionEntry?`; compute missing end pages from next entry start - 1
- [ ] 1.4 Add `TocPage` (int?) to `IngestionRecord` entity and add EF migration

## 2. TOC Map Extractor

- [ ] 2.1 Create `ITocMapExtractor` interface with `ExtractMapAsync(string tocPageText, CancellationToken)`
- [ ] 2.2 Implement `OllamaTocMapExtractor` — sends TOC page text to LLM, parses `{title, category, startPage, endPage}[]`, returns `IReadOnlyList<TocSectionEntry>`
- [ ] 2.3 Delete `ITocCategoryClassifier`, `OllamaTocCategoryClassifier`; update DI registration
- [ ] 2.4 Write unit tests for `TocCategoryMap.GetEntry` and end-page computation
- [ ] 2.5 Write unit tests for `OllamaTocMapExtractor` JSON parsing and fallback

## 3. Section Grouping

- [ ] 3.1 Create `PageBlockGrouper` static class with `Group(IReadOnlyList<PageBlock>)` returning `IReadOnlyList<IReadOnlyList<PageBlock>>` — h1/h2 starts new group, h3/body appended to current
- [ ] 3.2 Write unit tests for `PageBlockGrouper` covering: multiple h2s, no headings, h3 within section

## 4. Extraction Pipeline Changes

- [ ] 4.1 Add `entityName`, `sectionStartPage`, `sectionEndPage` parameters to `ILlmEntityExtractor.ExtractAsync`; update `OllamaLlmEntityExtractor` prompt to include context hint
- [ ] 4.2 Add `Trait` and `Lore` field schemas to `TypeFields` in `OllamaLlmEntityExtractor`
- [ ] 4.3 Update `IngestionOrchestrator.ExtractBookAsync` — read TOC page via `IPdfStructuredExtractor`, call `ITocMapExtractor`, iterate sections per page using `PageBlockGrouper`, pass entity context to extractor
- [ ] 4.4 Update `IngestionOrchestrator.ExtractSinglePageAsync` to use section grouping and TOC context
- [ ] 4.5 Update `OllamaLlmEntityExtractorTests` for new `ExtractAsync` signature
- [ ] 4.6 Update `IngestionOrchestratorTests` for section-level extraction flow

## 5. Registration API Changes

- [ ] 5.1 Add `tocPage` (int, required) to `RegisterBook` form handler; return 400 if missing
- [ ] 5.2 Delete `RegisterBookByPath` handler, `RegisterBookByPathRequest` record, and `MapPost("/books/register-path")` route
- [ ] 5.3 Update `DndMcpAICsharpFun.http` — add `tocPage` to register example, remove register-path example
- [ ] 5.4 Update `BooksAdminEndpointsTests` — add missing-tocPage-returns-400 test, remove register-path tests

## 6. Qdrant Payload Fields

- [ ] 6.1 Add `SectionTitle` (string?), `SectionStart` (int?), `SectionEnd` (int?) to `ChunkMetadata`
- [ ] 6.2 Add `section_title`, `section_start`, `section_end` constants to `QdrantPayloadFields`
- [ ] 6.3 Add `section_title` to keyword indexes and `section_start`/`section_end` to integer indexes in `QdrantCollectionInitializer`
- [ ] 6.4 Write `section_title`/`section_start`/`section_end` fields in `QdrantVectorStoreService`
- [ ] 6.5 Propagate `SectionTitle`/`SectionStart`/`SectionEnd` from `ExtractedEntity` through `JsonIngestionPipeline` to `ChunkMetadata`

## 7. ExtractedEntity Propagation

- [ ] 7.1 Add `SectionTitle` (string?), `SectionStart` (int?), `SectionEnd` (int?) to `ExtractedEntity`
- [ ] 7.2 Set these fields when constructing `ExtractedEntity` in `OllamaLlmEntityExtractor` (from the passed-in context params)
- [ ] 7.3 Pass through in `EntityJsonStore` serialisation/deserialisation
- [ ] 7.4 Update `EntityJsonStoreTests` and `JsonIngestionPipelineTests` for new fields
