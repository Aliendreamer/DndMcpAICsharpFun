## Why

Qdrant payload field names (`"source_book"`, `"version"`, `"category"`, `"entity_name"`, `"chapter"`, `"page_number"`, `"chunk_index"`, `"text"`) are repeated as raw string literals across four files. A single typo silently breaks write/read symmetry and is impossible to catch at compile time.

## What Changes

- Introduce a `QdrantPayloadFields` static class in `Infrastructure/Qdrant/` with one `public const string` per payload field
- Replace every raw string literal in `QdrantVectorStoreService`, `QdrantPayloadMapper`, `RagRetrievalService`, and `QdrantCollectionInitializer` with references to those constants

## Capabilities

### New Capabilities

None — this is a refactor with no observable behaviour change.

### Modified Capabilities

None — payload field name constants are an implementation detail. No existing requirement changes.

## Impact

- New: `Infrastructure/Qdrant/QdrantPayloadFields.cs`
- Modified: `Features/VectorStore/QdrantVectorStoreService.cs`
- Modified: `Features/Retrieval/QdrantPayloadMapper.cs`
- Modified: `Features/Retrieval/RagRetrievalService.cs`
- Modified: `Infrastructure/Qdrant/QdrantCollectionInitializer.cs`
- No API changes, no config changes, no new dependencies
