# Implementation Tasks

## 1. Domain enum and persistence

- [ ] 1.1 Create `Domain/BookType.cs` with `enum BookType { Unknown, Core, Supplement, Adventure, Setting }`.
- [ ] 1.2 Add `public BookType BookType { get; set; } = BookType.Unknown;` to `Infrastructure/Sqlite/IngestionRecord.cs`.
- [ ] 1.3 Add a new EF migration `Migrations/<timestamp>_AddBookTypeToIngestionRecord.cs` whose `Up` adds a nullable TEXT column named `BookType` to `IngestionRecords`, and `Down` drops it. Update `IngestionDbContextModelSnapshot.cs` to include the property.

## 2. Qdrant payload

- [ ] 2.1 Add `public const string BookType = "book_type";` to `Infrastructure/Qdrant/QdrantPayloadFields.cs`.
- [ ] 2.2 Update `Infrastructure/Qdrant/QdrantCollectionInitializer.cs` to include `BookType` in the keyword field list.
- [ ] 2.3 Add `BookType BookType` to `Features/VectorStore/BlockChunk.cs::BlockMetadata`.
- [ ] 2.4 Update `Features/VectorStore/QdrantVectorStoreService.cs::BuildBlockPoint` to write `point.Payload[QdrantPayloadFields.BookType] = meta.BookType.ToString();`.

## 3. Orchestrator plumbing

- [ ] 3.1 Update `Features/Ingestion/BlockIngestionOrchestrator.cs::IngestBlocksAsync` to read `record.BookType` and pass it through into each `BlockMetadata` constructor call.

## 4. Register endpoint

- [ ] 4.1 Update `Features/Admin/BooksAdminEndpoints.cs::RegisterBook` to read an optional `bookType` form field, call `Enum.TryParse<BookType>(value, ignoreCase: true, out var parsed)`, set `Unknown` on miss, and assign to `record.BookType`.
- [ ] 4.2 Add tests covering: valid value parses correctly, missing value defaults to Unknown, invalid value defaults to Unknown without HTTP 400.

## 5. Retrieval

- [ ] 5.1 Add `BookType? BookType` to `Features/Retrieval/RetrievalQuery.cs`.
- [ ] 5.2 Update `Features/Retrieval/RagRetrievalService.cs::BuildFilter` to add a `KeywordCondition(QdrantPayloadFields.BookType, query.BookType.Value.ToString())` when set.
- [ ] 5.3 Add the `bookType` query parameter to public and admin search endpoints in `Features/Retrieval/RetrievalEndpoints.cs`. Parse via `Enum.TryParse<BookType>(value, ignoreCase: true, out var parsed)`; pass into the `RetrievalQuery`. Unparseable → null (no filter).

## 6. Mapper

- [ ] 6.1 Add `BookType BookType` to `Domain/ChunkMetadata.cs` (default `Unknown`).
- [ ] 6.2 Update `Features/Retrieval/QdrantPayloadMapper.cs::ToChunkMetadata` to read the new field via a helper that returns `Unknown` when the payload key is absent or unparseable.

## 7. Documentation

- [ ] 7.1 Update `Config/appsettings.json` if any default needs to be exposed (likely none — bookType is per-book metadata, not config).
- [ ] 7.2 Update `DndMcpAICsharpFun.http`: add `bookType` to the register multipart block with a comment listing valid values; add at least one `&bookType=...` example to the retrieval section.

## 8. Tests

- [ ] 8.1 `BooksAdminEndpointsTests.cs`: add three cases covering bookType registration (valid / missing / invalid → Unknown).
- [ ] 8.2 `RagRetrievalServiceTests.cs`: add a case where setting `RetrievalQuery.BookType` produces a filter that includes the bookType keyword condition.
- [ ] 8.3 Verify existing tests still pass — no test should break because the field is additive with safe defaults.

## 9. Verification

- [ ] 9.1 `dotnet build` — zero errors, no new warnings.
- [ ] 9.2 `dotnet test` — all tests pass; new count up by ~5-7.
- [ ] 9.3 Manual smoke: register a small PDF with `bookType=Supplement`, ingest, query `?bookType=Supplement` and confirm results are limited to that book; query `?bookType=Core` and confirm results exclude it.
- [ ] 9.4 `openspec status --change add-book-type-classification` shows all four artifacts done.
