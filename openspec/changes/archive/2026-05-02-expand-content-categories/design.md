## Context

The ingestion pipeline classifies PDF pages into `ContentCategory` values and stores them as vector metadata in Qdrant. The current enum has 9 values: `Spell`, `Monster`, `Class`, `Background`, `Item`, `Rule`, `Treasure`, `Encounter`, `Trap`. `Rule` is overloaded — it covers combat rules, adventuring rules, conditions, divine lore, and planar lore. This causes poor retrieval precision and makes the TOC classifier return `null` for recognizable chapter types.

Two bugs were also found during live testing:
1. `PdfPigBookmarkReader` flattened all bookmark descendants (188 for PHB), causing the TOC Ollama call to produce invalid JSON. Fix: return only root + depth-1 nodes (~20 items).
2. `OllamaLlmEntityExtractor` only handled bare JSON arrays; the model sometimes returns `{"entities":[...]}`. Fix: unwrap single-key objects in `StripFences`.

## Goals / Non-Goals

**Goals:**
- Add `God`, `Combat`, `Adventuring`, `Condition`, `Plane`, `Race` to `ContentCategory`
- Update all LLM prompts and TypeFields to reflect the expanded list
- Fix the bookmark depth bug and the JSON unwrap bug
- Keep `Rule` as the catch-all for anything that doesn't fit a specific category

**Non-Goals:**
- Database migration (category is stored as a plain string — no schema change)
- Changing the embedding model or vector store structure
- Re-ingesting previously processed books (user can re-trigger extraction)

## Decisions

**All new categories use `description (string)` only**
Combat rules, conditions, gods, planes, and adventuring content are all prose. Adding structured fields (e.g. `damage_type` for Combat) would require the LLM to reliably extract them, which increases retry risk. Prose-only keeps extraction reliable and consistent with Background/Rule/Item.

**Bookmark depth = root + depth-1, not configurable**
D&D books consistently use a 2-level structure: Part → Chapter. Going deeper reintroduces the 188-item problem. Hardcoding 2 levels is simpler than a config value and matches all known target books.

**JSON unwrap in StripFences, not in caller**
Centralising the unwrap in `StripFences` means the fix applies to all current and future callers without changing the retry logic or JSON parsing upstream.

## Risks / Trade-offs

- [New categories might not appear in older books] → TOC classifier returns `null` for those chapters; pages are skipped (existing behaviour, no regression)
- [Re-ingestion required for existing books] → User must re-trigger extract + ingest-json for previously ingested books to benefit from new category tags; old chunks remain tagged as `Rule`
- [Model may still misclassify] → `Rule` catch-all ensures no data loss; worst case a Combat page is tagged Rule

## Migration Plan

1. Deploy new app image with enum + prompt changes
2. Re-trigger extraction for any previously ingested books via `POST /admin/books/{id}/extract`
3. Re-trigger JSON ingestion via `POST /admin/books/{id}/ingest-json`
4. No rollback complexity — category is a string field; old `Rule` chunks remain valid
