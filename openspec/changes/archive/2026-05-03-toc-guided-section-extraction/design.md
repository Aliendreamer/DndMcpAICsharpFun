## Context

The current extraction pipeline sends an entire PDF page as one LLM prompt and asks the model to produce a complete JSON entity array. On dense pages (Class features, Race traits) the model either times out generating a massive response or returns malformed JSON. The PHB's table of contents contains precise section boundaries — entity name, category, start page, and by inference end page — that we are not currently using.

The `ITocCategoryClassifier` exists but only returns `(startPage, category)` per entry, discarding the bookmark title and never computing end pages. PDF bookmarks are not available/reliable for the target books so TOC page text is the only source.

## Goals / Non-Goals

**Goals:**
- Parse one TOC page via LLM to produce a full `{title, category, startPage, endPage}[]` map
- Use that map to give the LLM entity-level context on every extraction call
- Group page blocks by heading so each LLM call covers one focused section
- Store section identity on Qdrant chunks for filtered retrieval
- Add `Trait` and `Lore` to `ContentCategory`
- Remove `register-path` endpoint; require `tocPage` on book registration

**Non-Goals:**
- Multi-page TOC support (one page is sufficient for PHB-style books)
- Automatic TOC page detection (user provides `tocPage`)
- Changing the embedding model or retrieval scoring logic

## Decisions

**D1: LLM computes end pages from TOC text**
The LLM sees the entire TOC at once and can infer end pages from adjacent start pages (Warlock=105, Wizard=113 → Warlock ends at 112). Alternative: compute end pages in code from sorted start pages. Chosen: LLM approach because the last entry's end page requires knowing the total page count, and the LLM can handle edge cases more flexibly. Code fallback: if LLM omits endPage, compute it from next entry's startPage - 1 in the map builder.

**D2: Replace `ITocCategoryClassifier` with `ITocMapExtractor`**
Rather than extending the existing classifier, introduce a new interface with a richer return type (`TocSectionEntry[]`). The old classifier took bookmarks; the new one takes page text. They are different enough to warrant separate types. `ITocCategoryClassifier` and `OllamaTocCategoryClassifier` are deleted.

**D3: Section grouping by heading level**
A "section" is an h1 or h2 block plus all consecutive body blocks that follow it until the next heading. h3 blocks are treated as sub-sections within the current section (included in the same group). Pages with no headings (pure body text) are sent as a single group. This keeps each LLM call small and focused without over-fragmenting content.

**D4: Extraction prompt carries entity context**
Each section extraction call includes: entity name from TOC map, category, page range. The LLM does not need to discover the entity name — it fills in the fields. This reduces hallucination and eliminates the wrong-shape JSON problem (model was inferring structure from headings instead of following schema).

**D5: `TocPage` stored on `IngestionRecord`**
Required at registration time, stored in SQLite. Enables re-extraction without re-registration. Migration adds nullable `TocPage INT` column; existing records get NULL (they will fail extraction, requiring re-registration — acceptable since this is a breaking change).

## Risks / Trade-offs

- **TOC page layout varies across books** → The LLM prompt must be generic enough to handle different formats. Fallback: if TOC map is empty, extraction fails with a clear error rather than silently producing nothing.
- **Section grouping may mis-group on unusual pages** → Accepted. Well-structured pages (Ch 2, 3, 6, 11) are the high-value targets; edge cases get an empty entity list which is better than a timeout.
- **More LLM calls per page** → A page with 10 sections makes 10 calls instead of 1. Each call is small and fast (~2-3s vs 14 min timeout). Net throughput is better.
- **Breaking change on registration API** → Existing registered books need re-registration with `tocPage`. Document clearly.

## Migration Plan

1. Add `TocPage INT` column to `IngestionRecord` via EF migration (nullable)
2. Deploy new code — existing books with NULL `TocPage` will fail on `/extract` with a clear error message pointing to re-registration
3. Re-register affected books with `tocPage` parameter
4. Wipe and re-extract (extraction output format unchanged, but section-level calls produce richer entity data)

No Qdrant volume wipe required — new `section_title`/`section_start`/`section_end` fields are additive.

## Open Questions

None — all decisions made during brainstorming.
