# llm-extraction (delta)

## REMOVED Requirements

### Requirement: ITocMapExtractor parses TOC page text into a structured map
**Reason**: TOC parsing via LLM is replaced by direct PDF bookmark extraction (`IPdfBookmarkReader` + `BookmarkTocMapper` in the ingestion pipeline). The `OllamaTocMapExtractor`, the `ITocMapExtractor` interface, the `TocSectionEntry`-from-text plumbing, and the TOC system prompt are deleted. The LLM is still used per-page for entity extraction; that path is unchanged.
**Migration**: No migration needed for callers — the extractor was an internal service. Callers of `IngestionOrchestrator` keep the same orchestrator interface; only the internals change.

### Requirement: Bookmark reader returns chapter-level nodes only
**Reason**: The new section-discovery path walks the bookmark tree recursively to obtain every entry, not just the top two levels. Limiting depth was only useful when bookmarks fed a TOC classifier prompt; with heuristic categorisation, the depth restriction is unnecessary and would cause us to lose subsection page boundaries.
**Migration**: `PdfPigBookmarkReader.ReadBookmarks` is updated to recurse the full tree. There is no caller-visible API change beyond the returned list potentially being longer.

### Requirement: TOC and entity prompts use full category list
**Reason**: With the TOC LLM prompt deleted, "TOC and entity prompts" reduces to "entity prompt" alone. The entity prompt continues to list all valid categories — that remains required, but is captured by the entity-extractor requirements that are not affected by this change.
**Migration**: None. Entity prompt requirements are unchanged.
