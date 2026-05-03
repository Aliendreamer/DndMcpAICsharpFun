## Why

The corpus is about to grow beyond the three core 2014 books. MM, DMG, Volo's, Xanathar's, Tasha's, MotM, setting books (Eberron, SCAG, Wildemount), and adventure modules (Curse of Strahd, Tomb of Annihilation) are all on the list. Today the only ways to slice the index by book identity are `version` (only meaningful for the 2014/2024 rules-edition split) and `source_book` (exact-match on display name). Neither helps with the question "give me only canonical rules content" vs "include lore/adventure flavour" — a distinction the consuming MCP agent will need to make to keep answers focused.

Adding an explicit `BookType` classification (Core / Supplement / Adventure / Setting / Unknown) closes that gap with a five-value orthogonal axis. It is **strictly additive**: no existing data is broken, the field is optional at registration, and unset rows simply read as `Unknown`. Future MCP tooling can compose the filter with the existing `version` and `source_book` filters to target exactly the slice it needs.

## What Changes

- New `BookType` enum in `Domain/`: `Core`, `Supplement`, `Adventure`, `Setting`, `Unknown` (default).
- `IngestionRecord` gains a `BookType BookType { get; set; } = BookType.Unknown;` property persisted as a string (EF SQLite default for enums).
- New EF migration adding the column. Nullable on the SQL side so existing rows don't break; the C# default ensures any newly created record is `Unknown` if the form field is missing.
- `BlockMetadata` (and therefore each Qdrant block point) gains a `BookType BookType` field; the orchestrator copies it from the record.
- `Infrastructure/Qdrant/QdrantPayloadFields.cs` gains a `BookType = "book_type"` constant.
- `QdrantCollectionInitializer` indexes `book_type` as a keyword field on the blocks collection (so filters are cheap).
- `QdrantVectorStoreService.BuildBlockPoint` writes the new payload field on every upsert.
- `RegisterBook` admin handler accepts an optional `bookType` form value, parses it via `Enum.TryParse<BookType>` (case-insensitive), and stores `Unknown` when missing or invalid.
- `RetrievalQuery` gains an optional `BookType?` filter; `RagRetrievalService.BuildFilter` adds a keyword filter when set; `RetrievalEndpoints` exposes a `bookType` query parameter on both public and admin search endpoints.
- `QdrantPayloadMapper.ToChunkMetadata` reads the new field back; `ChunkMetadata` gains it (defaulted to `Unknown`).
- `DndMcpAICsharpFun.http` documents the new field on the register example and on retrieval examples.
- Tests cover: register accepts a valid value, register accepts the field missing (→ `Unknown`), register rejects invalid value with `Unknown` (no failure — graceful default), retrieval filter applies when set, payload roundtrips through Qdrant.

## Capabilities

### New Capabilities
- `book-type-classification`: a small standalone capability defining the BookType taxonomy and the contract for tagging registered books and filtering retrieval results.

### Modified Capabilities
- `ingestion-pipeline`: the register endpoint accepts an additional optional form field; the ingestion orchestrator propagates the value into block metadata.
- `embedding-vector-store`: every Qdrant block point gains a `book_type` keyword payload field with a payload index.
- `rag-retrieval`: `/retrieval/search` and `/admin/retrieval/search` accept an additional optional `bookType` query parameter that filters by exact match.
- `http-contracts`: the `.http` file documents the new field on register and retrieval blocks.

## Impact

- **Code**: ~250 lines added across the touched files; ~30 lines of tests. No deletions.
- **Data migration**: one EF migration adding a nullable column. Existing rows in `IngestionRecords` keep working with the column reading as null on the SQL side, mapped to `BookType.Unknown` by EF.
- **Qdrant data**: existing points in `dnd_blocks` do not have the new payload field. They read back as `Unknown` via `QdrantPayloadMapper`'s default. Filtering for `bookType=Unknown` matches them; filtering for any other value excludes them. To tag pre-existing points, the operator deletes and re-ingests the book — same recipe as any retroactive metadata change.
- **API surface**: register gains one optional field; retrieval gains one optional query param. Both are additive, no breaking change.
- **Performance**: keyword index on `book_type`; filtering is O(1) lookup. No measurable cost.
- **Risk**: minimal. The default value path is explicit, the validation is permissive (invalid values silently become `Unknown` rather than rejecting the upload), and the field is orthogonal to every other piece of the system. Worst case: someone tags a book wrong and queries return slightly off content — fixable by re-registering.
