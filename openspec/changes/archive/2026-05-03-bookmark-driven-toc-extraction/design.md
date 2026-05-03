## Context

The current TOC extraction pipeline (introduced in `toc-guided-section-extraction`) reads the registered TOC PDF page, sends its raw text to an LLM via `OllamaTocMapExtractor`, and parses the JSON response into `TocSectionEntry` records. Investigation against a real PHB 2014 PDF showed PdfPig's text extraction interleaves the TOC's multi-column layout into unrecoverable garbage; the LLM consistently returns near-empty single objects and `TocCategoryMap` is empty, causing every page to be skipped.

The PHB (and almost all professionally-published D&D rulebooks) embed a bookmark/outline tree in the PDF that maps each section title to its starting page. PdfPig already exposes this via `document.TryGetBookmarks(...)`. An `IPdfBookmarkReader` and `PdfPigBookmarkReader` were prototyped in an earlier iteration, but were never wired into DI and are currently dead code.

## Goals / Non-Goals

**Goals:**
- Eliminate the LLM-on-TOC-page parsing step entirely.
- Use embedded PDF bookmarks as the deterministic source of section boundaries.
- Drop the `tocPage` registration field; users no longer need to know or specify a TOC page.
- Preserve current per-page entity extraction behavior (LLM is still called per page, that path is untouched).
- Maintain admin/MCP API ergonomics: registration becomes simpler, not more complex.

**Non-Goals:**
- Supporting bookmark-less PDFs gracefully (e.g., scanned books, stripped exports). These will fail extraction with a clear error; OCR / column-aware text recovery is out of scope.
- Changing how categories are *consumed* downstream (Qdrant payloads, retrieval filters). Only how they're *assigned* during ingestion changes.
- Re-introducing any LLM-based TOC parsing as a fallback. We accept the trade-off in favor of a simpler pipeline.

## Decisions

**1. Bookmarks as the sole TOC source.** Alternatives considered: (a) keep LLM TOC parsing as a fallback when bookmarks are missing; (b) use column-aware text reconstruction (group words by X coordinate). Rejected (a) because the LLM path is what we're fixing — keeping it as fallback means keeping all the dead-weight prompt and tests, and a fallback that "sometimes works" is worse than a clear failure mode. Rejected (b) because column reconstruction is fragile and PDF-specific; bookmarks are universal in our target corpus.

**2. Heuristic category mapping (no LLM call for categorization).** Alternatives considered: (a) one-shot LLM call on the bookmark title list, asking only for category labels. Rejected for the first iteration because the categories are mostly trivial keyword matches (the existing system prompt was effectively dressed-up keyword guidance). Heuristic mapping is deterministic, instant, free, and easy to tune. We can promote to a tiny LLM call later if real titles slip through.

**3. Recursive bookmark walker.** The existing `PdfPigBookmarkReader` only walks two levels (root + children). PHB-style books nest 3–5 levels deep (Part → Chapter → Section → Subsection). Walking the full tree gives us the most granular section boundaries, which produces tighter `endPage` ranges and more accurate per-page section assignment.

**4. Drop `TocPage` from `IngestionRecord` via EF migration.** Alternatives considered: keep the column nullable and unused. Rejected because leaving a dead column invites future confusion ("why is this nullable? when do we set it?") and the migration cost is trivial (one `DropColumn`). The migration is reversible — `Down()` re-adds the nullable column.

**5. Books with no bookmarks fail at extraction time, not at registration.** Alternatives considered: validate bookmarks at registration. Rejected because bookmark inspection requires opening the full PDF (slower) and the registration handler is now intentionally minimal (streaming upload only). A registered book whose bookmarks are missing will fail in `ExtractBookAsync` with `MarkFailedAsync(reason: "PDF has no embedded bookmarks; bookmark-driven extraction requires them.")`.

**6. Delete rather than deprecate the LLM TOC code.** Per project preference for clean removals over backwards-compat shims. The capability isn't documented as part of any external contract, only the registration form field and the debug endpoint, both of which we're removing.

## Risks / Trade-offs

- **[Risk]** A PHB-class PDF distributed without bookmarks (some pirated/stripped versions) will register fine but fail at extract-time. → **Mitigation:** clear error message in the failure record steering the user toward a properly-bookmarked source. Document the requirement in the README/API docs.
- **[Risk]** Bookmark titles in some PDFs may be inconsistent with content categories (e.g., a chapter titled "Adventuring Gear" should map to Item, not Adventuring). → **Mitigation:** keyword heuristic prioritizes specific keywords (`equipment`, `gear`, `weapons` → Item) before generic ones. Categorization is one-shot at extraction time and easy to re-run by re-extracting.
- **[Trade-off]** Removing the `tocPage` field is a breaking API change to `POST /admin/books/register`. → **Mitigation:** caller list is just our own admin tooling and the `.http` examples; no external consumers. Update all in the same change.
- **[Trade-off]** Heuristic categorization may misclassify edge cases that a 7B LLM might handle better. → **Mitigation:** we're not regressing — the current LLM path produces *zero* categorized entries on real PHB data. Heuristic guarantees correct categories for the obvious 80%+; the failure mode is "uncategorized" (null), which is recoverable downstream.
- **[Risk]** EF migration that drops a column is non-reversible at the data level even if `Down()` re-adds the column (data is gone). → **Mitigation:** the column held only redundant user-supplied integers; no information is lost that we care about.
