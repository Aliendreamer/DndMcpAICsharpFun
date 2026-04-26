## Context

Four files share a set of Qdrant payload field names as raw string literals: `QdrantVectorStoreService` (writes), `QdrantPayloadMapper` (reads), `RagRetrievalService` (filter keys), and `QdrantCollectionInitializer` (index creation). The strings are identical across all four — a rename or typo in one file silently breaks the others at runtime.

## Goals / Non-Goals

**Goals:**
- Single source of truth for every Qdrant payload field name
- Compile-time catch for any key mismatch between write and read paths
- Zero behaviour change — all existing data in Qdrant remains valid

**Non-Goals:**
- Changing field names or payload schema
- Abstracting Qdrant behind a generic key-value interface
- Replacing `DndVersion` / `ContentCategory` `.ToString()` serialisation

## Decisions

### D1 — `static class` with `const string` fields, not an enum

```csharp
// Infrastructure/Qdrant/QdrantPayloadFields.cs
namespace DndMcpAICsharpFun.Infrastructure.Qdrant;

public static class QdrantPayloadFields
{
    public const string Text       = "text";
    public const string SourceBook = "source_book";
    public const string Version    = "version";
    public const string Category   = "category";
    public const string EntityName = "entity_name";
    public const string Chapter    = "chapter";
    public const string PageNumber = "page_number";
    public const string ChunkIndex = "chunk_index";
}
```

Alternatives considered:
- **Enum with `[EnumMember]`**: requires a custom serialiser or `.GetAttributeValue()` helper to recover the string; adds ceremony for no gain since these strings never appear in a switch or comparison against each other.
- **`static readonly string`**: heap-allocated, no compiler inlining. `const` is substituted at compile time and is the idiomatic choice for fixed string keys.

### D2 — Placement in `Infrastructure/Qdrant/`

The constants describe the physical Qdrant document schema — an infrastructure concern. `Features/VectorStore/` and `Features/Retrieval/` already depend on `Infrastructure/Qdrant/` (via `QdrantOptions`, `QdrantClient`), so no new cross-layer dependency is introduced.

## Risks / Trade-offs

- **`const` inlining**: If the string values ever need to change, all assemblies referencing the constants must be recompiled. This project is a single assembly, so it is a non-issue.
- **No runtime protection**: Constants don't prevent passing the wrong constant to the wrong method — but that's a code-review concern, not a typing concern. The important gain is typo elimination.

## Migration Plan

1. Create `QdrantPayloadFields.cs`
2. Replace literals in each file one at a time, rebuilding after each
3. `dotnet build` must pass with 0 warnings at every step
4. No data migration needed — field names are unchanged
