## Why

We adopted MinerU for layout-aware **table recognition**, then throw its tables away. MinerU returns tables in `content_list` as `{ "type":"table", "table_body":"<html>…", "table_caption":[…], "page_idx":N }`, but our pipeline discards them in three places: `MinerUBlock` never deserializes `table_body`; `MinerUPdfConverter` intentionally drops `table` blocks; and `PdfConversionDiskCache` caches the already-stripped output. Result: **all 8 books have zero table items** — the Draconic Ancestry breath table, class-feature tables, treasure tables, etc. are gone. The downstream machinery to use tables already exists (`StructuredFactProjector` projects `CanonicalTable`s into Postgres; `CharacterResolutionService` resolves against them) — it's just never fed. This is a converter bug, not a MinerU limitation, and fixing it means structured tables come from **our own PDF read** (prose-grounded, works for homebrew), deterministically, with no LLM.

## What Changes

- Read MinerU's `table_body`/`table_caption`; stop dropping `table` blocks — emit them as `table` structure items carrying the HTML + page.
- A deterministic `HtmlTableParser` turns a MinerU table's HTML into a `CanonicalTable` (columns + rows of provenance-carrying cells). No LLM — MinerU already recognized the structure.
- Extraction collects those tables into `CanonicalJsonFile.Tables`, so the existing `CanonicalJsonWriter` serializes them and `StructuredFactProjector` projects them into Postgres on `ingest-entities`.

## Capabilities

### New Capabilities

- `mineru-table-extraction`: MinerU-recognized tables are preserved and deterministically parsed into `CanonicalTable`s carrying per-cell prose provenance, flowing into the canonical JSON and Postgres via the existing projector — structured tables sourced from our own PDF read.

### Modified Capabilities

<!-- none: additive to ingestion; existing entity/table-projection requirements unchanged -->

## Impact

- `Features/Ingestion/Pdf/MinerUPdfConverter.cs` + `MinerUBlock` (read `table_body`/caption, stop dropping tables).
- `Features/Ingestion/Pdf/PdfStructureDocument.cs` — `PdfStructureItem` gains an optional `Html` field.
- New `HtmlTableParser` (deterministic HTML → `CanonicalTable`).
- Extraction gains a table-collection step → `CanonicalJsonFile.Tables` (feeds existing `StructuredFactProjector`).
- No `.http`/`.insomnia` change (internal ingestion). No GPU/LLM.
- **Run step (fast, not GPU-days):** re-convert a book through MinerU (cache-busted) to populate real tables. **Out of scope / follow-on:** deriving `ChoiceSet`s from tables (needed for the full Dragonborn breath resolution) and 5etools cross-validation.
