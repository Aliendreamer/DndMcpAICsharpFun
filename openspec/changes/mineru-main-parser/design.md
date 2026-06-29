## Context

The `mineru-pdf-parser` spike (2026-06-29) explored the parse space and converged on an optimal recipe,
validated four-ways on the PHB. The recipe — MinerU `pipeline` backend, `-m ocr`, plus a spell-chapter
splitter — is settled; this design is about productionizing it (running MinerU automatically) without
re-opening the parse decisions. The pipeline downstream of `IPdfStructureConverter`
(`EntityCandidateScanner` → resolver → 5etools allowlist gate) is unchanged.

## Goals / Non-Goals

**Goals:**
- MinerU becomes the default, automatic structure converter; no manual CLI step.
- Preserve the spike's results (12/12 classes, 7/9 races, ~323/361 spells, zero noise).
- Keep conversions cached so the ~60–90 min/book OCR cost is paid once.
- Keep Marker as a safe, config-selectable fallback.

**Non-Goals:**
- Chasing the long-tail (38 missing PHB spells, 2 missing races) — deferred to the roadmap.
- The VLM/`hybrid-engine` backend (ruled out in the spike).
- Re-deriving the parse recipe (settled).

## Decisions

**1. MinerU runs as a service, called over HTTP — mirroring Marker.** A MinerU container exposes the
`mineru-api` FastAPI; `MinerUPdfConverter` POSTs the PDF path and polls/receives the structured output,
exactly as `MarkerPdfConverter` does against `marker:5002`. *(Alternative: shell out to the CLI from the
app — rejected: couples the app to a Python/torch/model install and the GPU.)*

**2. Backend = `pipeline`, method = `ocr` (fixed, configurable).** Pipeline keeps the class/race
section headings; `-m ocr` bypasses the corrupt embedded text layer. Both are `MinerUOptions` with
these defaults. *(Alternative: `auto`/`txt` — rejected, they inherit the garbled embedded text;
`hybrid-engine` — rejected in the spike.)*

**3. The spell-chapter splitter stays in the converter** (already implemented + tested): anchor on
`Casting Time:` blocks, read back the level/school line, cut the spell name at the first digit/school,
emit a synthetic `section_header`. It is parser-output post-processing, independent of how MinerU runs.

**4. Conversion is disk-cached.** A MinerU cache keyed by the PDF content hash (like
`PdfConversionDiskCache`) so `extract-entities --force` re-runs the LLM without re-paying the OCR.

**5. MinerU is the default; Marker is the fallback.** `MinerU:Enabled` defaults true; when false (or
the MinerU service is unreachable and a fallback is enabled) the existing Marker disk-cache converter
is used. Marker code is retained, not deleted.

## Risks / Trade-offs

- **The MinerU GPU service image** is the main unknown → must source or build an image bundling MinerU
  + torch + the pipeline/OCR models with GPU access; resolve in Task 1 (a spike of the service itself).
  If no clean image exists, fall back to a thin wrapper that runs the CLI inside the MinerU container.
- **OCR cost (~60–90 min/book) on the 8 GB GPU** → mitigated by disk caching (one-time per book) and by
  conversion/extraction being sequential (qwen3 isn't loaded during conversion). Re-converting the
  corpus is a deliberate one-time op.
- **OCR's own small error rate** (e.g. `PRIMAl` for `PRIMAL`) → far below the corrupt-encoding garble;
  the 5etools fuzzy matcher absorbs minor name errors.
- **Service unavailability** → Marker fallback keeps ingestion working.

## Migration Plan

Deploy = add the MinerU service + rebuild the app. Re-convert + re-extract each official book through
MinerU, then ingest the corrected canonical. Rollback = set `MinerU:Enabled=false` (instant revert to
Marker). The spike's `docker-compose.override.yml` + `.mineru-spike/` are removed.

## Open Questions

- Exact MinerU service image / GPU wiring (Task 1 resolves). Everything else is settled by the spike.
