## Why

We've added `books/System Reference Document.pdf` (SRD 5.0, released under OGL 1.0a — the **2014** ruleset). The SRD is the openly-licensed subset of the 2014 PHB/DMG/MM, all three of which are already ingested (`dnd_entities`=2310, with 388 SRD-flagged via 5etools). Before committing any SRD content to retrieval, we want the **structured extraction output** in hand to judge overlap and value. The entity-handling decision is deliberately deferred until we can read that output.

## What Changes

This is a **process/runbook change — no code, schema, or API changes.** It uses the existing ingestion endpoints and records a decision gate.

- Register `books/System Reference Document.pdf` (no `fivetoolsSourceKey` — the SRD is not a 5etools source key → IDs mint from the display name → slug `system-reference-document`).
- Run `POST /admin/books/{id}/extract-entities` to produce `books/canonical/system-reference-document.json` (+ optional sibling `.errors.json` / `.warnings.json`) directly from Marker conversion of the PDF.
- **Review gate:** inspect the canonical JSON, then record a go/no-go decision on what to do with the SRD entities. The dedup/overlap strategy (the SRD largely duplicates `phb14.*` / `mm14.*` / `dmg14.*`) is **explicitly out of scope here** — it becomes its own change if we decide to proceed.

**Explicit non-goals:**
- Do **not** run `ingest-blocks` → nothing is written to `dnd_blocks`.
- Do **not** run `ingest-entities` → nothing is written to `dnd_entities`.
- No code, schema, or endpoint changes.

## Capabilities

### New Capabilities
- `extraction-vector-store-isolation`: Pins down the durable safety contract this runbook relies on — `extract-entities` produces only canonical JSON on the filesystem and writes to **neither** Qdrant collection; `ingest-blocks` is the sole writer of `dnd_blocks` and `ingest-entities` the sole writer of `dnd_entities`. This is what makes "extract a book for review without populating retrieval" a safe, supported operation.

### Modified Capabilities
<!-- None. The entity-extraction-pipeline capability already specifies extract-entities is decoupled from ingest-blocks; this change adds the explicit no-vector-write guarantee as a new capability rather than modifying that one. -->
- _(none)_

## Impact

- **Filesystem:** new `books/canonical/system-reference-document.json` (+ optional `.errors.json` / `.warnings.json`).
- **Database:** one new `IngestionRecord` row for the registered book.
- **Qdrant:** unchanged — `dnd_blocks` (17,571) and `dnd_entities` (2,310) are not touched.
- **No code, no migrations, no API surface change.**
- **Cost:** extraction is the slow qwen3 step; the ~360-page SRD is plausibly a multi-hour run (checkpointed every 100 candidates, crash-resumable).
