## Context

We have a working LLM-driven ingestion pipeline (`/extract` → JSON → `/ingest-json` → Qdrant `dnd_chunks` collection) that produces high-quality, structured entities. On this hardware (`qwen2.5:7b` partially CPU-offloaded), a 300+ page book takes hours to fully process because the LLM is invoked per page.

PdfPig exposes `DocstrumBoundingBoxes` (a layout-aware page segmenter from O'Gorman 1993) plus `UnsupervisedReadingOrderDetector` — together they produce ordered, multi-column-aware text blocks suitable for direct embedding. Bookmarks already give us section titles and starting pages (`IPdfBookmarkReader` + `BookmarkTocMapper` from the `bookmark-driven-toc-extraction` change). Combining these, we can produce a per-block Qdrant point without any LLM call: text comes from Docstrum, category/section metadata comes from bookmarks.

This is the standard architecture for MCP-backed RAG: the consumer is itself an LLM, so structured fields like "spell level" or "monster AC" can be inferred at query time from the block text plus metadata. We don't need to pre-extract them.

## Goals / Non-Goals

**Goals:**
- Build a parallel, opt-in ingestion path that produces an embedded Qdrant collection in minutes instead of hours.
- Keep the existing LLM path completely intact — both paths usable side-by-side.
- Make the choice of which collection retrieval queries a configuration switch, no code change required.
- Use Docstrum + reading-order detection so multi-column pages produce coherent block text.
- Reuse existing bookmark machinery for section/category metadata.

**Non-Goals:**
- Replacing the LLM path. After A/B comparison the user will decide what to keep; this change does not delete anything.
- Stat-block-aware segmentation (D&D monster stat blocks). Docstrum may segment those imperfectly; that is acceptable for a first iteration. Tuning is a follow-up.
- Cross-page block continuation merging. If a block runs from page N onto page N+1, each becomes its own Qdrant point. This is acceptable because retrieval pulls top-K matches and a well-phrased query will retrieve adjacent blocks together.
- Re-extracting books already in `dnd_chunks`. Block ingestion is run on demand per book; existing data is left untouched.

## Decisions

**1. Separate Qdrant collection over a payload flag in the same collection.**
Alternatives considered: (a) put both LLM-chunks and blocks in `dnd_chunks` distinguished by `extraction_mode` payload field. Rejected because it complicates retrieval (every query needs an extra filter), risks accidental mixing in result sets, and makes deletion of one mode (`docker compose down -v` notwithstanding) require a query-and-delete loop instead of a single `DeleteCollection`. Separate collections are clean and trivial to compare.

**2. Single-stage ingest endpoint, no JSON-on-disk intermediate.**
The LLM path uses two-stage (`/extract` → JSON → `/ingest-json`) because the LLM step is expensive and we want to be able to re-embed without re-extracting. The block path has neither expense; the cheapest design is one endpoint that does segmentation + embedding + upsert in one pass. If we ever want to inspect raw blocks, a dry-run mode can be added later.

**3. Configuration switch (not a query parameter) for which collection retrieval queries.**
Alternatives considered: a `?collection=blocks` query param on `/retrieval/search`. Rejected for the first iteration because the change author wants to A/B *which collection performs better in production*, not let every caller pick. A startup-time flag is the simplest way to bind one collection per deployment. We can add a per-query override later if we keep both modes long-term.

**4. Same vector size (1024, `mxbai-embed-large`) for both collections.**
We are not trying to A/B embedding models in this change — only ingestion strategies. Identical embedding model means apples-to-apples retrieval comparison. If we want to compare embedders, that's a separate change.

**5. Bookmarks remain mandatory for the block path.**
A book whose PDF lacks bookmarks falls into the same failure mode as the LLM path post-`bookmark-driven-toc-extraction`: extraction fails with a clear error. We don't try to derive sections from text geometry alone; that's a deeper rabbit hole and bookmarks are present for ~all real-world D&D PDFs.

**6. New work-item type, not endpoint-only.**
The block path runs through the same `IngestionQueueWorker` as extract/ingest-json so a single book can't have two stages racing each other. This also gives us cancellation, status logging, and the same conflict-handling behaviour.

**7. Docstrum default parameters.**
We use `DocstrumBoundingBoxes.Instance` defaults for now. Tuning the within-line / between-line / between-column multipliers is a follow-up if the first run shows obvious mis-segmentation on D&D layouts.

## Risks / Trade-offs

- **[Risk]** Docstrum may segment D&D stat blocks (monsters with multi-column attribute tables) into many small fragments, hurting retrieval. → **Mitigation:** measured later against real queries; if quality degrades, follow-up change to either special-case stat-block detection or post-process Docstrum output.
- **[Risk]** Block-level chunks lack typed fields (spell level, damage dice). Queries that need exact filters ("3rd level fire spells only") work less well. → **Mitigation:** acceptable trade-off given the consumer is an LLM that can do this filtering at query time. If unacceptable later, fall back to LLM path or add a hybrid retrieval mode.
- **[Risk]** Two collections in Qdrant doubles storage. → **Mitigation:** trivial in absolute terms (a few MB per book × few books). Both can be deleted independently when not needed.
- **[Trade-off]** A book without bookmarks fails the block path identically to the LLM path. We're not building a fallback for bookmark-less PDFs in either pipeline.
- **[Trade-off]** Cross-page block continuations are not merged. This is fine for top-K semantic retrieval but would matter if a downstream consumer needed full-section reassembly. None do today.
- **[Trade-off]** Adding `Retrieval:Collection` is a small surface increase in configuration. Defaults preserve current behaviour, so no operator action is required when this change ships.
