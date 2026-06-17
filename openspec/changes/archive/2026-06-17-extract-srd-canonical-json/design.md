## Context

The structured-entity-extraction pipeline already supports producing canonical JSON directly from a PDF. `EntityExtractionOrchestrator.ExtractAsync` Marker-converts the registered book's `FilePath`, scans candidates, runs qwen3, and writes `books/canonical/<slug>.json` (+ optional `.errors.json` / `.warnings.json`). It does **not** read or write any Qdrant collection — `dnd_blocks` is populated only by `ingest-blocks`, and `dnd_entities` only by `ingest-entities`. So "produce the SRD canonical JSON without populating `dnd_blocks`" requires only the existing `register` + `extract-entities` endpoints; no new code.

The SRD 5.0 PDF (`books/System Reference Document.pdf`) is the 2014 ruleset under OGL 1.0a — the open subset of the 2014 PHB/DMG/MM we already hold. Its entities therefore largely duplicate `phb14.*` / `mm14.*` / `dmg14.*`. We want the extraction output before deciding whether/how to use any of it.

## Goals / Non-Goals

**Goals:**
- Produce `books/canonical/system-reference-document.json` (+ siblings if any) for review.
- Leave Qdrant untouched: `dnd_blocks`=17,571 and `dnd_entities`=2,310 unchanged.
- Record an explicit go/no-go decision on the SRD entities at a review gate.

**Non-Goals:**
- No `ingest-blocks` (no `dnd_blocks` writes).
- No `ingest-entities` (no `dnd_entities` writes).
- No code, schema, migration, or API changes.
- No dedup/overlap strategy — deferred to a separate change if we proceed.

## Decisions

- **Register with no `fivetoolsSourceKey`.** The SRD is not a 5etools source key; content is `srd:true`-flagged on PHB/DMG/MM instead. Omitting the key makes the system mint opaque IDs from the display name "System Reference Document" → slug `system-reference-document`. This keeps SRD IDs distinct from `phb14.*`/etc., which is what we want while the entities sit in a review-only JSON.
- **`extract-entities` only.** Deliberately skip `ingest-blocks` and `ingest-entities` so nothing reaches Qdrant. The canonical JSON on disk is the sole artifact.
- **Review gate is a hard stop.** After the JSON is produced, work pauses for human review + a recorded decision. No automated follow-on ingestion.
- **Verification is collection counts.** Confirm `dnd_blocks` and `dnd_entities` point counts are unchanged after the run as proof nothing was ingested.

## Risks / Trade-offs

- **Runtime.** Extraction is the slow qwen3 step; a ~360-page SRD may take multiple hours. Mitigated by the pipeline's checkpointing (every 100 candidates, crash-resumable) — a stack restart resumes rather than restarts.
- **Duplicate content (deferred, not solved here).** The produced entities will largely mirror existing 2014 books. That's expected; this change only surfaces them for inspection. Acting on the overlap is a separate decision.
- **Root-owned canonical files.** Past runs wrote canonical files owned by root inside the container; if that recurs, a `chown` to the host user may be needed to read/commit the JSON. Operational, not a code risk.
