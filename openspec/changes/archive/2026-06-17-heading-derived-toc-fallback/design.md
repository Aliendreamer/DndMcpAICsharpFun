## Context

`EntityExtractionOrchestrator.ExtractAsync` builds candidates in four steps: Marker-convert the PDF → read PDF bookmarks → `BookmarkTocMapper.Map(bookmarks)` → `TocCategoryMap` → `EntityCandidateScanner.Scan(inputs, tocMap)`. The scanner looks up each section's earliest page in the page-keyed `TocCategoryMap`; `null` → `ContentCategory.Unknown` → `MapCategoryToEntityType` returns `null` → the section is skipped.

`TocCategoryMap` is sparse and range-based: it sorts `TocSectionEntry(Title, Category, StartPage, EndPage?)` by `StartPage` and back-fills each entry's `EndPage` to the next entry's `StartPage − 1` (last entry → `int.MaxValue`). A category therefore propagates across all pages until the next entry overrides it. PDF bookmarks are themselves sparse (chapter/section granularity), which is why this works.

`BookmarkTocMapper.GuessCategory(title)` is a private keyword classifier (`Contains`/`ContainsAny` over a fixed keyword set) returning a `ContentCategory`, defaulting to `Rule` when nothing matches. It also has a parent-title fallback for leaf bookmarks (e.g. monster names under "Monsters (A-Z)").

Marker emits `PdfStructureItem(string Type, string Text, int PageNumber, int? Level)`; headings are `Type == "section_header"` with a `Level` (observed 1/3/4 in the SRD). These are already available as `doc.Items` inside `ExtractAsync` and are consumed by `BuildScannerInputs`.

## Goals / Non-Goals

**Goals:**
- Bookmark-less PDFs produce entity candidates instead of an empty canonical JSON.
- Reuse the existing deterministic keyword classifier — no new LLM usage in category recognition.
- Zero observable change for bookmarked books.

**Non-Goals:**
- LLM-based section classification (Approaches B/C) — deferred.
- Using the `Level` field in the first cut (kept as a documented future refinement).
- Re-running SRD extraction (separate operational step).
- Any change to `EntityCandidateScanner`, `TocCategoryMap`, or `MapCategoryToEntityType`.

## Decisions

- **Shared classifier.** Extract `GuessCategory` (and its `Contains`/`ContainsAny` helpers) into a shared static classifier — e.g. `HeadingCategoryClassifier.Guess(title)` in `Features/Ingestion/Pdf`. `BookmarkTocMapper` delegates to it (its parent-title fallback stays in `BookmarkTocMapper`, since headings have no parent concept). This keeps one keyword source of truth and guarantees the bookmark path's output is byte-for-byte identical.

- **`HeadingTocMapper.Map(IReadOnlyList<PdfStructureItem> headings) → IReadOnlyList<TocSectionEntry>`.** For each `section_header` item, classify its `Text`. **Emit an entry only when the category is confident** — i.e. not `Rule` and not `Unknown` (and skip blank titles). This sparse emission is essential: a keyword-less sub-heading like "Rage" or "Hill Dwarf" classifies to `Rule` and must be dropped so it does not reset the surrounding range. The result feeds the unchanged `TocCategoryMap`, whose propagation fills the gaps.

- **Orchestrator wiring.** After `BookmarkTocMapper.Map`, construct the `TocCategoryMap`. If it `IsEmpty`, rebuild `tocEntries` from `doc.Items.Where(i => i.Type == "section_header")` via `HeadingTocMapper`, construct a new `TocCategoryMap`, and log the real fallback. The bookmark branch is otherwise untouched.

- **Confident category set.** Confident = any `ContentCategory` that `MapCategoryToEntityType` maps to a non-null `EntityType` (Spell, Monster, Class, Race, Background, Item, Condition, God, Plane, Treasure, Trap). Non-entity categories (Rule, Combat, Adventuring, Encounter, Trait, Lore, Unknown) are dropped — emitting them would create ranges the scanner skips anyway and could wrongly truncate a real category's range.

- **Corrected log.** `PdfPigBookmarkReader`'s "falling back to all-categories extraction" message is replaced/relocated so the actual behavior — heading-derived TOC fallback (or genuinely no signal) — is logged accurately.

## Risks / Trade-offs

- **Keyword fuzziness.** Heading classification is imperfect (e.g. a "Spellcasting" sub-heading under a class could open a spurious `Spell` range). Accepted for the first cut: mis-typed candidates surface in `errors.json`/review and are tunable. The `Level` field is the escape hatch (prefer higher-level boundaries) if propagation proves noisy — explicitly deferred.
- **Coverage vs precision.** Sparse confident-only emission favors clean propagation over catching every section; some content may fall under a neighboring category's range. This mirrors how sparse bookmarks already behave, so it is consistent with the existing pipeline.
- **No signal case.** A PDF with neither bookmarks nor confident headings yields 0 candidates — logged clearly as the genuine outcome, not a crash.
