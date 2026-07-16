## Context

`BlockIngestionOrchestrator.IngestBlocksAsync` reads a PDF's embedded bookmark outline, builds a `TocCategoryMap` via `BookmarkTocMapper`, then attaches `(section_title, category, section_start, section_end)` metadata to each MinerU-extracted block by page. If the bookmark tree is empty it aborts: `MarkFailedAsync(NoBookmarksError)`. MPMM (id 8) and EEPC (id 9) hit this — both have real printed structure but no embedded `/Outlines` object.

The entity-extraction path (`EntityCandidateBuilder`) already handles this: when bookmarks are empty it derives a TOC from MinerU `section_header` items via `HeadingTocMapper`. That mapper is deliberately **sparse/confident-only** — it drops headings it can't confidently categorize so they don't reset the enclosing page-range category (correct for *classification*, where uncovered pages simply yield no candidate). Block ingestion cannot reuse it directly: for *retrieval*, an uncovered page means a **dropped block** (`GetEntry(page) is null → continue`), i.e. lost content.

The enabling data is already in hand and thrown away: `StructureBlockExtractor.ExtractBlocksAsync` converts the PDF (disk-cached via `PdfConversionDiskCache`), iterates `doc.Items`, and keeps only prose text — discarding the `section_header` items. Surfacing them costs one extra list, no extra conversion.

## Goals / Non-Goals

**Goals:**

- Block ingestion never hard-aborts solely because a PDF lacks an embedded outline.
- When bookmarks are absent, **every** prose block is retained and retrievable (full page coverage — nothing silently dropped).
- Fine-grained section titles (each heading becomes a `SectionTitle`) with best-effort categories.
- The bookmarked path is byte-for-byte unchanged (zero regression).
- The new TOC logic is a pure, unit-testable function.

**Non-Goals:**

- Changing bookmarked-book behavior in any way.
- Reusing or altering `HeadingTocMapper` (extraction path stays sparse/confident-only).
- Fixing the pre-existing `block-extraction` spec drift (PdfPig/`BoundingBox`).
- Resolving EEPC's keyless `SourceKey=null` story.
- The post-ingest BM25 corpus-consistency re-ingest pass.
- Re-ingesting MPMM/EEPC (a later operational step, not part of this change).

## Decisions

**D1 — Surface headings from the extractor (Approach 2), not re-convert.**
`IPdfBlockExtractor.ExtractBlocksAsync` returns a new `PdfExtraction(IReadOnlyList<PdfBlock> Blocks, IReadOnlyList<PdfStructureItem> Headings)`. *Alternative considered:* inject `IPdfStructureConverter` into the orchestrator and call `ConvertAsync` again (cache hit). Rejected: it duplicates the conversion call and splits "turn this PDF into structured items" across two collaborators. The extractor already holds `doc.Items`; returning the headers is the honest one-conversion fix.

**D2 — A new `FullCoverageHeadingTocMapper`, separate from `HeadingTocMapper`.**
The two mappers have opposite contracts: `HeadingTocMapper` maximizes category precision by dropping uncertain headings; the new one maximizes coverage by keeping all of them. Conflating them behind a flag would make both harder to reason about. Keep them as two small, single-purpose functions.

**D3 — Coverage algorithm.** `Map(headings) → IReadOnlyList<TocSectionEntry>`:
- One entry per non-empty heading, `Title = heading.Text` (fine-grained).
- `Category = HeadingCategoryClassifier.Guess(text)` when confident; otherwise **carry forward** the last confident category; otherwise `ContentCategory.Rule`. (MPMM bestiary sub-headings inherit `Monster` from the enclosing "Monsters" anchor while keeping their own titles.)
- If the first heading starts after page 1, prepend `("Front Matter", Rule, page 1)`.
- If there are **no** headings, emit one entry `(book display name, Rule, page 1)` covering the whole book.
- `TocCategoryMap` then fills contiguous end-pages → coverage is `[1, ∞)` with no gap.

**D4 — Orchestrator change is additive and branch-local.** Only the empty-bookmarks branch changes: instead of `MarkFailedAsync`, build the fallback `TocCategoryMap` from `extraction.Headings`. The page→section lookup, long-block split, embed, BM25, and upsert are untouched. The `chunks.Count == 0` guard stays as the genuine "no prose at all" terminal failure, with a message distinct from the old bookmark error.

## Risks / Trade-offs

- **Generic category on un-anchored regions** → Mitigation: carry-forward keeps categories correct wherever a confident anchor exists; where none does, blocks still embed and are retrievable by prose + section title. Category is a filter hint, not a gate.
- **`TocCategoryMap` is page-granular: multiple headings on one page → only the last wins that page's range** → Mitigation: accept it. It is inherent to the existing page-keyed model (affects bookmarks too), and coverage still holds (every page maps to exactly one entry). Not worth a sub-page rework here.
- **Interface change ripples to tests** → Mitigation: exactly two test files (`StructureBlockExtractorTests`, `BlockIngestionOrchestratorTests`) and one DI line; all in-repo, compile-time caught (warnings-as-errors).
- **Front-matter catch-all may mislabel a real early section as "Front Matter"** → Mitigation: only applies to pages *before the first detected heading*; those blocks would otherwise be dropped entirely, so a generic label is strictly better than loss.

## Migration Plan

Pure code change, no data migration. Deploy = rebuild the app image. Rollback = revert; bookmarked books are unaffected either way. Re-ingesting MPMM/EEPC to exercise the new path is a separate, later operational step (and EEPC is gated on its source-key decision).

## Open Questions

- **`block-extraction` spec drift** (pre-existing): its spec describes a retired PdfPig/`BoundingBox` extractor, not the current MinerU `StructureBlockExtractor`. This change deliberately does not touch it; a future spec-sync change should reconcile it (and would then own the `PdfExtraction` return-shape requirement currently parked under `full-coverage-block-toc`).
- **EEPC `SourceKey=null`** (out of scope): the startup scope-health guard may flag null source keys once EEPC blocks land. Decide EEPC's key story before re-ingesting it.
