## Why

`sourceName` is a vestigial form field on `POST /admin/books/register`. It was originally a short tag (`"PHB"`, `"MM"`) used by LLM-extraction prompts for source-attribution context. After the LLM ingestion path was removed, no part of the active pipeline reads `sourceName`:

- It is stored on `IngestionRecord.SourceName` in SQLite.
- It is **not** propagated into `BlockMetadata`, **not** written to Qdrant payload, and **not** consulted by retrieval.
- Its only remaining surface is the `GET /admin/books` listing, which already shows `displayName`.

Meanwhile `displayName` covers every real need: it becomes `source_book` in the Qdrant payload, drives the `?sourceBook=…` retrieval filter, anchors metadata in retrieval responses, and is what the admin list shows by default. Two fields where one will do is the textbook definition of API noise — operators register a book and have to invent a value for a field that affects nothing.

Removing `sourceName` shrinks the registration surface, drops a vestigial SQLite column, and makes the data model match what the system actually uses.

## What Changes

- **BREAKING — `sourceName` is removed from `POST /admin/books/register`.** Callers that send the field will silently have it ignored (the multipart parser doesn't enforce unknown-field rejection). Callers that depend on the field for anything would already be silently broken since nothing reads it.
- Drop `IngestionRecord.SourceName` property and its `[Required, MaxLength(100)]` data annotations.
- Add an EF migration that drops the `SourceName` column from `IngestionRecords`.
- Drop the `sourceName` form-section parsing branch from `BooksAdminEndpoints.RegisterBook`.
- Update `DndMcpAICsharpFun.http` register example to remove the `sourceName` part.
- Update tests that pass `sourceName` to drop that line.
- No changes to Qdrant payload, retrieval, embedding, or any other capability.

## Capabilities

### New Capabilities
<!-- none -->

### Modified Capabilities
- `ingestion-pipeline`: register endpoint no longer accepts `sourceName`; record schema loses the column.
- `http-contracts`: `.http` register example loses the `sourceName` form part.

## Impact

- **Code:** ~20 lines deleted across `IngestionRecord.cs`, `BooksAdminEndpoints.cs`, the test fixtures, and `.http`. One new EF migration file (~25 lines).
- **API:** breaking only in the loosest sense — existing callers that sent `sourceName` will continue to register successfully (the field is silently ignored after this change). They lose the SQLite-side storage of that value.
- **Storage:** existing `SourceName` data is dropped. Qdrant is unaffected (the field was never in the payload).
- **Tests:** ~5-7 test methods touched to drop `sourceName` form parts; counts unchanged.
- **Risk:** minimal. The field was already a no-op for retrieval and ingestion logic; this just removes the dead surface.
