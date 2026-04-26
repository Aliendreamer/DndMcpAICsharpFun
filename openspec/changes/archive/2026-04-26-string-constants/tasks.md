## 1. Add QdrantPayloadFields constants

- [x] 1.1 Create `Infrastructure/Qdrant/QdrantPayloadFields.cs` with `public const string` for all 8 field names: `Text`, `SourceBook`, `Version`, `Category`, `EntityName`, `Chapter`, `PageNumber`, `ChunkIndex`
- [x] 1.2 Run `dotnet build` — must pass with 0 errors, 0 warnings

## 2. Replace literals in QdrantVectorStoreService

- [x] 2.1 Add `using DndMcpAICsharpFun.Infrastructure.Qdrant;` to `Features/VectorStore/QdrantVectorStoreService.cs`
- [x] 2.2 Replace all 8 string literals in `BuildPoint` with `QdrantPayloadFields.*` constants
- [x] 2.3 Run `dotnet build` — must pass with 0 errors, 0 warnings

## 3. Replace literals in QdrantPayloadMapper

- [x] 3.1 Add `using DndMcpAICsharpFun.Infrastructure.Qdrant;` to `Features/Retrieval/QdrantPayloadMapper.cs`
- [x] 3.2 Replace all 7 string literals in `ToChunkMetadata` and `GetText` with `QdrantPayloadFields.*` constants
- [x] 3.3 Run `dotnet build` — must pass with 0 errors, 0 warnings

## 4. Replace literals in RagRetrievalService

- [x] 4.1 Replace all 4 string literals passed to `KeywordCondition(...)` in `BuildFilter` with `QdrantPayloadFields.*` constants
- [x] 4.2 Run `dotnet build` — must pass with 0 errors, 0 warnings

## 5. Replace literals in QdrantCollectionInitializer

- [x] 5.1 Replace the `keywordFields` array literals with `QdrantPayloadFields.*` constants
- [x] 5.2 Replace the `intFields` array literals with `QdrantPayloadFields.*` constants
- [x] 5.3 Run `dotnet build` — must pass with 0 errors, 0 warnings

## 6. Final verification

- [x] 6.1 Run `dotnet clean && dotnet build` — 0 errors, 0 warnings
- [x] 6.2 Verify no remaining raw string literals for payload keys: `grep -rn '"source_book"\|"version"\|"category"\|"entity_name"\|"chunk_index"\|"page_number"\|"chapter"' Features/ Infrastructure/ --include="*.cs"` must return no results
