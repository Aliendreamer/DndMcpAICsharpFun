## Why

Our entity store holds 2310 LLM-extracted entities across four books. The extraction is book-faithful (real prose, page-accurate) but OCR-noisy in structured values — garbled CRs, spell levels, AC, tags. 5etools has the same entities as clean, structured data but no narrative voice. We have both on disk (`books/canonical/*.json` and `./5etools/`). Today the 5etools infrastructure can only bulk-*import* 5etools entities (adding thousands of unrelated records and overwriting prose). We want the "best of both worlds": our prose + 5etools' clean structured fields, applied to the entities we already have — without importing 5etools-only records.

## What Changes

- Enrich entities at **ingest time**, not by bulk import. For each entity being ingested, look up its matching 5etools record (by aligned id) directly from the local `5etools/` files via the existing mappers — without upserting any 5etools-only record into `dnd_entities`.
- **Extend `EntityMerger`** so 5etools wins clean structured/scalar values inside `fields` (CR, spell level, AC, components, type tags) while our narrative arrays (`entries` and prose-bearing keys, per a small per-type allowlist) are preserved. SRD flags and keywords already come from 5etools; add a rule that 5etools' clean `name` wins unless the entity is `dataSource=="manual"`.
- **BREAKING (merge behavior):** `fields` is no longer "canonical always wins" — it becomes a deep merge. Our prose is preserved; OCR-noisy structured values are replaced by 5etools' clean ones.
- Source 5etools records from **files** rather than requiring a prior store import, so enrichment never adds 5etools-only entities.
- Ingest result **reports** entities enriched, matched-to-5etools, and unmatched (coverage visibility).
- Apply by re-ingesting the four books (DMG, Tasha, PHB, MM).

## Capabilities

### New Capabilities

- `fivetools-entity-enrichment`: at ingest, match each entity to a 5etools record loaded from the local `5etools/` files and enrich it; enrichment-only (no 5etools-only entities added); report match coverage.

### Modified Capabilities

- `entity-merge`: `fields` becomes a deep merge where 5etools wins structured/scalar values and our narrative arrays are preserved; add a `name` rule (5etools clean name wins unless `dataSource=="manual"`).

## Impact

- Code: new 5etools-record lookup (reuse `FivetoolsSourceRegistry` + the 18 mappers to build an id→envelope index from files); extend `EntityMerger.Merge` with deep `fields` merge + name rule; `EntityIngestionOrchestrator` sources the "existing" 5etools envelope from the file index instead of (or in addition to) the store; ingest report gains enrichment counts.
- Data: re-ingest DMG/Tasha/PHB/MM; `dnd_entities` stays 2310 (no new entities), but matched entities gain clean structured fields + flags + keywords; `dnd_blocks` unaffected.
- No new dependencies; 5etools files already present. No bulk `/admin/5etools/import` behavior change required (that path stays for full imports).
