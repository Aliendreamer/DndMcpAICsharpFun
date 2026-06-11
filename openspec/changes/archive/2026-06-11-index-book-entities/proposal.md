## Why

Both registered books have their prose fully block-indexed (`dnd_blocks` = 6,898 points) but **zero** structured entities (`dnd_entities` = 0). The DMG is stuck in `EntitiesExtracting` after an interrupted run (no canonical JSON, no checkpoint on disk), and Tasha's never started extraction. Structured entity lookups — a core companion capability — return nothing until these books are extracted and ingested.

## What Changes

- Extract structured entities for both registered books via the existing Ollama-driven pipeline, then ingest them into `dnd_entities`.
- For the DMG's stuck `EntitiesExtracting` status, use `?force=true` to override and re-run (no recovery code added).
- Add a spec requirement clarifying that `extract-entities?force=true` overrides ANY prior status (including a stuck `EntitiesExtracting`/`EntitiesIngesting`) and overwrites existing canonical JSON.

## Capabilities

### New Capabilities
<!-- none -->

### Modified Capabilities
- `entity-extraction-pipeline`: adds a requirement that `force=true` overrides a stuck extraction status.

## Impact

- Operational run against the live stack: `POST /admin/books/{id}/extract-entities` then `/ingest-entities` for DMG (force) and Tasha.
- Produces `books/canonical/<slug>.json` for both books (reviewable artifacts).
- Populates `dnd_entities` in Qdrant.
- Long-running: qwen3:8b ~20–65 s/candidate → hours for the DMG corpus.
