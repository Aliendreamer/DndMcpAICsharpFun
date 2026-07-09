## Why

The extraction grounding gate was designed as a three-tier cascade (Tier 0 OCR field-match →
Tier 1 embedding → Tier 2 qwen3 judge), but only **Tier 0 shipped**. Everything the cheap
field-match can't confirm is dumped into `NeedsReview`, so the backlog is large and cleared only
by hand. At the same time, the parked prose-grounded rethink showed the real risk is the
opposite: *fabricated* entities (the Dragonborn "Monster" stat block) that Tier 0 can't catch and
that a naive topical check would wrongly wave through. We need Tiers 1 and 2 built — as one shared
grounder — so extraction grades entities better AND a backlog pass can auto-resolve the pile
without rubber-stamping fabrications.

## What Changes

- Add a shared **`GroundingCascade`** service returning a **`GroundingVerdict`**
  `{ Status: Grounded | Ungrounded | Uncertain, DecidedByTier, Score }`, composed as a pure
  function of (Tier 0 bool, Tier 1 score vs floor, Tier 2 verdict).
- **Tier 1 (new, embedding):** embed the entity's text and query `dnd_blocks` (mxbai vectors)
  scoped to the entity's **own book + a page window** around its page. Below a configured
  similarity floor → `Ungrounded` (no supporting prose → fabrication candidate), short-circuit.
  Above the floor without Tier-0 field confirmation → **escalate**. Tier 1 is an **escalation
  gate only** — topical similarity alone NEVER promotes (the guard against fabrication passing).
- **Tier 2 (new, opt-in qwen3 judge):** asked specifically *"are these emitted fields supported
  by this prose?"* over the entity's source chunk → `Grounded` or `Ungrounded` (fabrication).
  Behind an interface (reuse the Ollama/qwen3 client abstraction) so it is fake-testable.
- **Verdict → action ("clear + flag fabrications"):** `Grounded` → auto-clear `NeedsReview`
  (promote to `Accepted`), subject to the **name gate**; `Ungrounded` (Tier-2-confirmed, or Tier-1
  floor-reject when the judge is enabled) → new **`EntityDisposition.Ungrounded`** (auditable,
  excluded from `dnd_entities`, kept in canonical — never hard-deleted); `Uncertain` → stays
  `NeedsReview`.
- **Name gate:** reuse `ExtractionNeedsReview.HasOcrArtifacts`. Grounding reasons
  (`low-confidence`/`ungrounded`) are driven by the verdict; an `ocr-artifact` (garbled-name)
  entity auto-clears **only if** content grounds AND the name no longer has an OCR artifact — a
  garbled name alone keeps it flagged.
- **Extraction-time integration:** `EntityExtractionRunner.BuildTypedEnvelope` swaps its
  Tier-0-only `HasGroundedContent` for the shared cascade (Tier 0→1, Tier 2 on the residual when
  the run enables the judge), feeding the verdict into `ExtractionDispositionPolicy`. The
  disposition *contract* is unchanged; a stronger grounder sits behind it.
- **Backlog pass:** `POST /admin/books/{id}/reground-entities` runs the cascade over that book's
  `NeedsReview` entities; `?judge=true` opts into Tier 2 (else Tier 0/1 only, fast, no LLM);
  checkpointed/resumable via `<slug>.reground.progress.json` (mirrors `extract-entities`); writes
  canonical (clear flag / set `Ungrounded`) and targeted Qdrant re-index per changed entity
  (reuse `ReindexEntityAsync`); returns `{ scanned, promoted, markedUngrounded, stillFlagged,
  tier2Invoked }`.
- Update `DndMcpAICsharpFun.http` and `dnd-mcp-api.insomnia.json` for the new endpoint.

## Capabilities

### New Capabilities

- `entity-grounding-cascade`: the shared `GroundingCascade` service, `GroundingVerdict`, Tier 1
  embedding grounding (own-book + page-window scoping, escalation-gate-only), Tier 2 judge
  interface, the verdict→action policy (promote / flag-ungrounded / leave-flagged + the name
  gate), and the `POST /admin/books/{id}/reground-entities` backlog endpoint.

### Modified Capabilities

- `extraction-grounding-gate`: Tiers 1 and 2 are concretely implemented via the shared cascade;
  the gate delegates to `GroundingCascade`. Tier 1 is scoped to the entity's own book + page
  window and is an escalation gate only (topical similarity never grounds on its own).
- `extraction-disposition`: add `Ungrounded` to the enumerated dispositions — a judge-confirmed
  ungrounded fabrication, distinct from a model-chosen `Declined`; excluded from `dnd_entities`,
  retained in canonical for audit.

## Impact

- **Code:** new `GroundingCascade` + `GroundingVerdict` + Tier 1 embedding grounder + Tier 2 judge
  interface (all in `Features/Ingestion/EntityExtraction/`); `EntityExtractionRunner` and
  `ExtractionDispositionPolicy` updated; new `EntityDisposition.Ungrounded`; new admin endpoint +
  reground service reusing `ReindexEntityAsync`; checkpoint file plumbing.
- **APIs:** one new admin endpoint (`X-Admin-Api-Key` guarded); no breaking changes.
- **Data:** the reground pass mutates `NeedsReview`/`Disposition` in canonical + re-indexes changed
  entities; canonical files are edited in place (existing resolve/normalize pattern), never deleted.
  A new checkpoint sidecar `<slug>.reground.progress.json`.
- **Cost/perf:** Tier 2 (qwen3) is opt-in and runs only on the escalated residual; Tier 0/1 stay
  cheap. Backlog runs are checkpointed/resumable like extraction.
- **Docs:** `DndMcpAICsharpFun.http` + `dnd-mcp-api.insomnia.json`.
