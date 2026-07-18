## Context

`MinerUPdfConverter` POSTs a PDF to MinerU with `return_content_list=true` and maps the returned `content_list` blocks onto `PdfStructureItem`s. It keeps `text` (→ `text`) and heading blocks (→ `section_header`) and **drops everything else** — including `table`. `MinerUBlock` deserializes only `type/text/text_level/page_idx`, so even if we stopped dropping, the table HTML isn't captured. And `PdfConversionDiskCache` persists the mapped (stripped) `PdfStructureDocument`, so cached books have no recoverable table data.

The target shape already exists and is consumed downstream:
- `CanonicalTable(Id, Name, Columns, Rows)`, `CanonicalTableRow(Cells)`, `CanonicalCell(Value, Provenance)`, `ProvenanceRef(BlockId, SourceBook, Page)` — the exact shape hand-authored in `dragonborn-slice.json`.
- `CanonicalJsonFile.Tables` → `StructuredFactProjector` → Postgres `StructuredTables`/`StructuredTableRows` → `CharacterResolutionService`.

So this change only has to get MinerU's tables into `CanonicalJsonFile.Tables`; everything after is built.

## Goals / Non-Goals

**Goals:**

- MinerU-recognized tables survive conversion as structured items carrying their HTML + page.
- Deterministically parse that HTML into `CanonicalTable`s with per-cell provenance (no LLM).
- Populate `CanonicalJsonFile.Tables` during extraction so the existing projector lands them in Postgres.

**Non-Goals:**

- **No LLM/GPU** table interpretation — MinerU already did the vision/structure work; we parse its HTML.
- Deriving `ChoiceSet`s from tables (the full Dragonborn breath resolution needs one) — documented follow-on.
- 5etools cross-validation of parsed tables — follow-on; 5etools demotes to a checker, not the source.
- Homebrew-specific handling — the path is book-agnostic; homebrew simply benefits for free.

## Decisions

**D1 — Preserve, don't drop.** `MinerUBlock` gains `table_body` (string HTML) and `table_caption` (string[]). In the converter's block loop, a `table` block becomes a `PdfStructureItem` of type `"table"` with `Text` = the joined caption and a new `Html` field = `table_body`, at the block's page. `PdfStructureItem` gains a trailing optional `string? Html = null` (existing constructions unaffected). Image/equation/header/footer stay dropped.

**D2 — Deterministic `HtmlTableParser`.** Parse the well-formed MinerU `<table>` HTML with a focused, dependency-free tokenizer: split on `<tr>…</tr>`, then cells on `<t[hd]>…</t[hd]>`; strip inner tags, decode entities, collapse whitespace (OCR-tolerant). The first row supplies the `Columns` (fallback to `col0…colN` if it has no `<th>` and looks like data); remaining rows → `CanonicalTableRow`s. `rowspan`/`colspan` handled minimally (a spanning cell's value repeated across the span; if absent, cells are padded) — documented as the known limitation. Malformed/empty HTML → `null`/skip (never throw).

**D3 — Provenance per cell from the table's page.** Every parsed `CanonicalCell` gets `ProvenanceRef(BlockId = "<bookslug>.block.table.<n>", SourceBook = record.SourceKey, Page = table page)`. Cell-precise provenance isn't available from a single HTML block (the design's known "hard part"), so all cells in a table share the table's page/block — honest and sufficient for citation. `CanonicalTable.Id = "<bookslug>.table.<slug(caption)>"` (a positional suffix disambiguates duplicate/absent captions); `Name` = caption.

**D4 — Collect during extraction.** The extraction path already builds `CanonicalJsonFile` from the converted document. Add a step that filters the converted items for type `"table"`, runs `HtmlTableParser`, and sets `CanonicalJsonFile.Tables`. Deterministic, runs alongside the LLM entity extraction, independent of it (a table needs no LLM). The writer already serializes `Tables`; `ingest-entities` already projects them.

## Risks / Trade-offs

- **Cache holds stripped output** → the fix only takes effect on a **re-convert** (cache-busted). Mitigation: documented run step; MinerU conversion is minutes/book (service-side, not qwen3/GPU-days).
- **Messy tables** (nested/rowspan/multi-header, OCR noise) → parse to a best-effort grid; malformed → skipped, logged. The grounding/consumer side treats table cells as citeable text, so an imperfect grid still beats a dropped table. Refine parser as real MinerU output is seen.
- **Table id collisions** (repeated captions, e.g. many "Traits" tables) → positional suffix in the id; acceptable — tables are addressed by id, and the projector upserts by `CanonicalId`.
- **Non-table `table` blocks** (MinerU mis-tags a layout region as a table) → they still parse to a grid and land as a `CanonicalTable`; low harm (a spurious table is inert until referenced). A caption/shape sanity filter can be added if noise proves high.
