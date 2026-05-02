# LLM-Assisted PDF Extraction

## Goal

Replace raw PDF text chunking with a two-pass LLM extraction pipeline that produces structured JSON entities (Spell, Monster, Class, etc.) before ingesting into Qdrant. Each entity becomes one semantically coherent chunk with accurate metadata.

## Architecture

Two independent stages, each triggered by an admin endpoint:

```
PDF
 │
 ▼
[Stage 1: Extract]  POST /admin/books/{id}/extract
 │  Pass 1 per page → LLM classifies which entity types are present
 │  Pass 2 per page → LLM extracts each detected type with a focused prompt
 │  Output → {booksPath}/extracted/{bookId}/page_{n}.json
 │
 ▼
[Stage 2: Ingest JSON]  POST /admin/books/{id}/ingest-json
 │  Reads all page JSON files for the book
 │  Runs adjacent-page merge pass for partial entities
 │  Embeds each entity's description with nomic-embed-text
 │  Upserts into Qdrant with full metadata payload
 │
 ▼
Qdrant (same collection, structured payloads)
```

The existing registration flow (`POST /admin/books/register-path`) stays unchanged — it creates the SQLite record. Extract and ingest-json are explicit next steps.

## New Components

### `ILlmClassifier`
Pass 1. Takes a page's text, returns `IReadOnlyList<ContentCategory>` of entity types detected on that page. One Ollama chat call per page using a short classification prompt. Returns empty list for sparse pages.

### `ILlmEntityExtractor`
Pass 2. Takes a page's text and one `ContentCategory`, returns `IReadOnlyList<ExtractedEntity>`. One Ollama chat call per detected type per page.

### `IEntityJsonStore`
Reads/writes per-page JSON files to `{booksPath}/extracted/{bookId}/page_{n}.json`. Provides:
- `SavePageAsync(bookId, pageNumber, entities)`
- `LoadAllPagesAsync(bookId)` → ordered list of page entity lists
- `RunMergePassAsync(bookId)` → merges partial entities across adjacent pages by matching name

### `IJsonIngestionPipeline`
Orchestrates Stage 2: loads all pages via `IEntityJsonStore`, runs merge pass, embeds each entity's description, upserts to Qdrant via `IVectorStoreService`.

## JSON Schema

Each page file is a JSON array. All entities share a common envelope:

```json
[
  {
    "page": 42,
    "source_book": "Player's Handbook 2014",
    "version": "Edition2014",
    "partial": false,
    "type": "Spell",
    "name": "Fireball",
    "data": { ... }
  }
]
```

### Type-specific `data` shapes

**Spell** (Phase 1):
```json
{
  "level": 3,
  "school": "Evocation",
  "casting_time": "1 action",
  "range": "150 feet",
  "components": "V, S, M (a tiny ball of bat guano and sulfur)",
  "duration": "Instantaneous",
  "description": "..."
}
```

**Monster** (Phase 1):
```json
{
  "size": "Large",
  "type": "Dragon",
  "alignment": "Chaotic Evil",
  "ac": 17,
  "hp": "178 (17d10+85)",
  "speed": "40 ft., fly 80 ft.",
  "abilities": { "str": 23, "dex": 10, "con": 21, "int": 14, "wis": 11, "cha": 19 },
  "description": "..."
}
```

**Class** (Phase 1):
```json
{
  "hit_die": "d10",
  "primary_ability": "Strength",
  "saving_throws": ["Strength", "Constitution"],
  "armor_proficiencies": "All armor, shields",
  "weapon_proficiencies": "Simple and martial weapons",
  "features": [{ "level": 1, "name": "Second Wind", "description": "..." }]
}
```

**Background, Item, Rule, Treasure, Encounter, Trap** (Phase 2):
```json
{ "description": "..." }
```

The `description` field on every entity type is what gets embedded. All other fields become Qdrant payload metadata.

## LLM Prompts

### Pass 1 — Classification

```
System:
You are a D&D 5e content classifier. Given a page of text from a D&D rulebook,
list only the entity types present on this page. Reply with a JSON array of strings
using only these values: Spell, Monster, Class, Background, Item, Rule, Treasure,
Encounter, Trap. Reply with [] if no entities are found. Reply with JSON only, no
explanation.

User: <page text>
```

### Pass 2 — Extraction (one call per detected type)

```
System:
You are a D&D 5e content extractor. Extract all <TYPE> entities from the page text
below. Return a JSON array of objects. Each object must have:
- name (string)
- partial (bool — true if the entity appears cut off at the page boundary)
- data (object with the fields listed below)

Use null for any missing fields. Reply with JSON only, no explanation.

Fields for <TYPE>: <type-specific field list>

User: <page text>
```

## Ollama Configuration

`OllamaOptions` gains `ExtractionModel` (default: `llama3.2`), separate from `EmbeddingModel`. The `ollama-pull` Docker init container pulls both models on startup.

## New Admin Endpoints

```
POST /admin/books/{id}/extract        — fires Stage 1 in background
GET  /admin/books/{id}/extracted      — lists page JSON files produced (count + file paths)
POST /admin/books/{id}/ingest-json    — fires Stage 2 from existing JSON files
```

## Status Flow

`IngestionStatus` gains two new values:

| Status | Meaning |
|---|---|
| `Extracted` | JSON files on disk, not yet in Qdrant |
| `JsonIngested` | Fully ingested from structured JSON into Qdrant |

Full flow: `Pending → Processing → Extracted → JsonIngested`

## Error Handling

| Situation | Behaviour |
|---|---|
| Pass 1 returns invalid JSON | Log warning, skip page, continue |
| Pass 2 returns invalid JSON | Log warning, save raw page text as `Rule` entity with `partial: true` — nothing silently dropped |
| Sparse page (0 chars) | Skip both passes |
| Extraction failure | Mark record `Failed` with error message; JSON files already saved remain on disk |
| Ingest-JSON failure | Mark record `Failed`; JSON files untouched — fix and retry without re-extracting |

## Merge Pass

Before ingesting, `IEntityJsonStore.RunMergePassAsync` scans adjacent page files. If `page_n` has entity X with `partial: true` and `page_n+1` has an entity of the same type and name, concatenate their `description` fields and drop the duplicate from `page_n+1`.

## What Does Not Change

- `POST /admin/books/register` and `POST /admin/books/register-path` — unchanged
- `DELETE /admin/books/{id}` — unchanged (also deletes extracted JSON files)
- `PdfPigTextExtractor` — still used by Stage 1 to get page text before LLM calls
- `DndChunker` — stays for now; replaced as the new pipeline proves out
- Qdrant collection schema — same collection, same vector size

## Requirements

### Requirement: Bookmark reader returns chapter-level nodes only
The bookmark reader SHALL return only root-level nodes and their immediate children (depth 0 and depth 1). Deeper descendants SHALL be excluded to keep the TOC classification prompt manageable.

#### Scenario: Nested bookmarks return two levels
- **WHEN** a PDF has a bookmark tree with root → chapter → section hierarchy
- **THEN** the reader SHALL return root and chapter nodes only, excluding section nodes

#### Scenario: Flat bookmarks are unaffected
- **WHEN** a PDF has only root-level bookmarks with no children
- **THEN** the reader SHALL return all root nodes

### Requirement: Entity extractor unwraps single-key JSON objects
The entity extractor SHALL unwrap responses of the form `{"<key>": [...]}` (a JSON object with exactly one key whose value is an array) before attempting to parse the array. This handles the case where the model wraps the result array in an object.

#### Scenario: Wrapped array is unwrapped transparently
- **WHEN** the model returns `{"entities": [{"name": "Fireball", ...}]}`
- **THEN** the extractor SHALL parse it as if the model returned `[{"name": "Fireball", ...}]`

#### Scenario: Bare array is unchanged
- **WHEN** the model returns a bare JSON array `[{"name": "Fireball", ...}]`
- **THEN** the extractor SHALL parse it directly without modification

### Requirement: TOC and entity prompts use full category list
Both the TOC classifier system prompt and the entity extractor system prompt SHALL list all valid categories including the six new values. The TOC prompt SHALL include concrete mapping examples for the new categories.

#### Scenario: TOC prompt includes new categories
- **WHEN** the TOC classifier is invoked
- **THEN** its system prompt SHALL list `God`, `Combat`, `Adventuring`, `Condition`, `Plane`, `Race` as valid categories alongside existing ones
