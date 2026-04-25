## 1. Embedding Service

- [x] 1.1 Create `Features/Embedding/IEmbeddingService.cs` with `EmbedAsync(IList<string> texts, CancellationToken ct) → Task<IList<float[]>>`
- [x] 1.2 Create `Features/Embedding/OllamaEmbeddingService.cs` implementing `IEmbeddingService` using `OllamaApiClient`
- [x] 1.3 Map OllamaSharp embed response to `float[]` arrays in correct input order
- [x] 1.4 Add error wrapping: catch `HttpRequestException` and rethrow with Ollama-identifying message
- [x] 1.5 Register `IEmbeddingService → OllamaEmbeddingService` as scoped in `Program.cs`

## 2. Vector Store Service

- [x] 2.1 Create `Features/VectorStore/IVectorStoreService.cs` with `UpsertAsync` (signature includes fileHash for deterministic IDs)
- [x] 2.2 Create `Features/VectorStore/QdrantVectorStoreService.cs` implementing `IVectorStoreService` using `QdrantClient`
- [x] 2.3 Implement deterministic point ID derivation: `Guid` from `SHA256(fileHash + chunkIndex.ToString())`
- [x] 2.4 Map all `ChunkMetadata` fields to Qdrant `Payload` dictionary
- [x] 2.5 Call `QdrantClient.UpsertAsync` with the full batch of points
- [x] 2.6 Register `IVectorStoreService → QdrantVectorStoreService` as scoped in `Program.cs`

## 3. Qdrant Collection Initialiser

- [x] 3.1 Create `Infrastructure/Qdrant/QdrantCollectionInitializer.cs` implementing `IHostedService`
- [x] 3.2 In `StartAsync`: call `QdrantClient.CollectionExistsAsync`; if false, call `QdrantClient.CreateCollectionAsync` with vector size from `QdrantOptions:VectorSize` and cosine distance
- [x] 3.3 Log success or skip (collection already exists) at information level
- [x] 3.4 Register `QdrantCollectionInitializer` as hosted service in `Program.cs` — registered BEFORE `IngestionBackgroundService`

## 4. Payload Index Creation

- [x] 4.1 In `QdrantCollectionInitializer.StartAsync`, after collection creation, create keyword payload indexes for: `source_book`, `version`, `category`, `entity_name`
- [x] 4.2 Create integer payload indexes for: `page_number`, `chunk_index`

## 5. Embedding Ingestor (Replace Stub)

- [x] 5.1 Create `Features/Embedding/EmbeddingIngestor.cs` implementing `IEmbeddingIngestor`
- [x] 5.2 Accept `IList<ContentChunk>` and the book's file hash
- [x] 5.3 Split chunks into batches of `IngestionOptions:EmbeddingBatchSize`
- [x] 5.4 For each batch: call `IEmbeddingService.EmbedAsync`, then `IVectorStoreService.UpsertAsync`
- [x] 5.5 Replace the no-op `IEmbeddingIngestor` registration in `Program.cs` with `EmbeddingIngestor`

## 6. Configuration

- [x] 6.1 `VectorSize` (768) and `CollectionName` (`dnd_chunks`) already in `QdrantOptions`
- [x] 6.2 `EmbeddingBatchSize` (32) already in `IngestionOptions`
- [x] 6.3 `appsettings.json` already has all fields

## 7. Verification

- [x] 7.1 `dotnet build` passes with zero errors
- [ ] 7.2 Start Docker Compose stack; confirm `QdrantCollectionInitializer` logs collection created
- [ ] 7.3 Register and ingest one PDF via admin endpoints
- [ ] 7.4 Query Qdrant directly (via `GET /collections/dnd_chunks/points/scroll`) and confirm points exist with correct payload fields
- [ ] 7.5 Re-ingest the same PDF and confirm point count does not increase (upsert deduplication)
