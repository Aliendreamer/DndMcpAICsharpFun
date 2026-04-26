## 1. Add QdrantPayloadFields constants

- [ ] 1.1 Create `Infrastructure/Qdrant/QdrantPayloadFields.cs` with `public const string` for all 8 field names: `Text`, `SourceBook`, `Version`, `Category`, `EntityName`, `Chapter`, `PageNumber`, `ChunkIndex`
- [ ] 1.2 Run `dotnet build` — must pass with 0 errors, 0 warnings

## 2. Replace literals in QdrantVectorStoreService

- [ ] 2.1 Add `using DndMcpAICsharpFun.Infrastructure.Qdrant;` to `Features/VectorStore/QdrantVectorStoreService.cs`
- [ ] 2.2 Replace all 8 string literals in `BuildPoint` with `QdrantPayloadFields.*` constants
- [ ] 2.3 Run `dotnet build` — must pass with 0 errors, 0 warnings

## 3. Replace literals in QdrantPayloadMapper

- [ ] 3.1 Add `using DndMcpAICsharpFun.Infrastructure.Qdrant;` to `Features/Retrieval/QdrantPayloadMapper.cs`
- [ ] 3.2 Replace all 7 string literals in `ToChunkMetadata` and `GetText` with `QdrantPayloadFields.*` constants
- [ ] 3.3 Run `dotnet build` — must pass with 0 errors, 0 warnings

## 4. Replace literals in RagRetrievalService

- [ ] 4.1 Replace all 4 string literals passed to `KeywordCondition(...)` in `BuildFilter` with `QdrantPayloadFields.*` constants
- [ ] 4.2 Run `dotnet build` — must pass with 0 errors, 0 warnings

## 5. Replace literals in QdrantCollectionInitializer

- [ ] 5.1 Replace the `keywordFields` array literals with `QdrantPayloadFields.*` constants
- [ ] 5.2 Replace the `intFields` array literals with `QdrantPayloadFields.*` constants
- [ ] 5.3 Run `dotnet build` — must pass with 0 errors, 0 warnings

## 6. Final verification

- [ ] 6.1 Run `dotnet clean && dotnet build` — 0 errors, 0 warnings
- [ ] 6.2 Verify no remaining raw string literals for payload keys: `grep -rn '"source_book"\|"version"\|"category"\|"entity_name"\|"chunk_index"\|"page_number"\|"chapter"' Features/ Infrastructure/ --include="*.cs"` must return no results
