## Context

Grounding today is Tier 0 only: `EntityExtractionRunner.BuildTypedEnvelope` calls
`HasGroundedContent(fields, candidate.Text)` (true when ≥1 significant field value OCR-fuzzy-matches
the source prose via `Tier0FieldGrounding.IsTextGrounded`) and feeds the boolean into
`ExtractionDispositionPolicy.Derive(grounded, name, confidence)` → `Accepted` / `NeedsReview` /
`Declined`. The `extraction-grounding-gate` spec already committed to a Tier 0→1→2 cascade, but
Tiers 1–2 were never built, so anything Tier 0 can't confirm lands in `NeedsReview` and is cleared
only by the hand-triage endpoints (`needsreview-triage`).

Two prose sources exist: `dnd_blocks` (prose chunks, mxbai-embed-large vectors) and `dnd_entities`
(rendered entities). Entities carry `Page`, `SourceBook`, `Edition`, and `Fields`. The
`ReindexEntityAsync` path already re-renders + re-embeds a single entity and upserts it (used by
`resolve`), so targeted re-index after a canonical edit is a solved problem.

Constraints: canonical JSON under `books/canonical/` is the hand-correctable source of truth —
edited in place (as `resolve`/`normalize` do), never deleted by automation. Tier 2 (qwen3:8b) is
slow (~30 s/candidate), so any pass that uses it must be opt-in and checkpointed. The parked
prose-grounded rethink warns that the dangerous failure is *fabrication that is topically similar
to real prose* — so a topical embedding match must never, by itself, certify an entity.

## Goals / Non-Goals

**Goals:**

- One shared, testable `GroundingCascade` returning a graded verdict, used by both extraction and a
  backlog re-grounding pass.
- Auto-resolve the `NeedsReview` backlog conservatively: promote confidently-grounded entities,
  flag judge-confirmed fabrications, leave the uncertain middle for humans.
- Implement Tiers 1–2 the grounding-gate spec already specified, with Tier 1 scoped so it cannot
  false-certify a fabrication.

**Non-Goals:**

- Re-architecting the structured-entity layer (that is the parked `prose-grounded-knowledge-model`
  effort). This change strengthens grounding within the current model.
- Fixing garbled names (that is name-normalization). Grounding never rewrites a name; the name gate
  only decides whether a grounded entity may still be promoted.
- Hard-deleting entities. `Ungrounded` is an auditable disposition; canonical files are edited in
  place, never removed.
- A corpus-wide single-call reground (per-book, mirroring the extraction pipeline, was chosen).

## Decisions

### `GroundingVerdict` is a pure function of the three tiers

`GroundingCascade.Grade(entity, sourceProse, options)` returns
`GroundingVerdict { Status: Grounded | Ungrounded | Uncertain, DecidedByTier: 0|1|2, Score }`. The
combination logic is pure and table-testable; the tiers themselves are injected (Tier 1 embedder +
vector store, Tier 2 judge), so the decision function is unit-tested without I/O. *Alternative:*
returning a bare bool (as Tier 0 does today) — rejected because auto-promotion vs auto-flag needs
to distinguish "confidently grounded", "confidently fabricated", and "unsure".

### Tier ordering and short-circuits

1. **Tier 0** — `Tier0FieldGrounding` over emitted fields. Any field confirms → **Grounded** (tier
   0), stop. (Cheapest, highest-precision positive signal.)
2. **Tier 1** — embed the entity's text (its `CanonicalText`, or a render of its fields when empty)
   and query `dnd_blocks` filtered to `SourceBook == entity.SourceBook` and `Page` within
   `±PageWindow` of `entity.Page`. Let `s` = top similarity.
   - `s < Floor` → **Ungrounded** (tier 1): no supporting prose in the entity's own neighbourhood.
   - `s ≥ Floor` (but Tier 0 did not confirm) → **escalate** (topically present, field support
     unverified).
   - Tier 1 **never** returns `Grounded` — topical similarity alone is exactly the fabrication
     blind spot. *Alternative:* promote on high `s` — rejected (Dragonborn fabrication would pass).
3. **Tier 2** — judge, only if enabled and reached: prompt the qwen3 judge with the entity's
   emitted fields + the source prose and ask whether the fields are supported → `Grounded` or
   `Ungrounded` (tier 2). If the judge is disabled or unsure, the residual stays **Uncertain**.

Page-window + same-book scoping (not corpus-wide) blunts cross-entity false grounding; `Floor` and
`PageWindow` are config knobs (`GroundingOptions`).

### Verdict → action, with the name gate

- **Grounded** → clear `NeedsReview` / set `Accepted`, **unless** the entity's reason is
  `ocr-artifact` (`ExtractionNeedsReview.HasOcrArtifacts(name)` true) — then it stays flagged
  (content is real but the name is still garbage; name-normalization owns that).
- **Ungrounded** → set `EntityDisposition.Ungrounded` (kept in canonical, excluded from
  `dnd_entities`). Only reachable when the judge ran (or Tier 1 floor-rejected *with* judge
  enabled) — we never auto-flag a fabrication on Tier 1 alone without the judge, to stay
  conservative on destructive-ish actions.
- **Uncertain** → unchanged (`NeedsReview`).

### Shared use at extraction time

`BuildTypedEnvelope` replaces `HasGroundedContent` with `GroundingCascade.Grade(...)`, mapping the
verdict to the existing disposition inputs (grounded bool for the policy, plus the new `Ungrounded`
status). Extraction passes `candidate.Text` as the source prose (already in hand), so Tier 1's
vector query is only needed when Tier 0 fails — same short-circuit. Tier 2 at extraction time runs
only if the run opts in (keeps the default extraction cost unchanged).

### Backlog pass mirrors the extraction pipeline

`POST /admin/books/{id}/reground-entities?judge=false` → `RegroundService`:
load canonical → select `NeedsReview` entities → per entity build source prose (fetch the entity's
`dnd_blocks` neighbourhood, or its stored source) → `GroundingCascade.Grade` → apply verdict→action
→ on change, write canonical + `ReindexEntityAsync`. Checkpoint `<slug>.reground.progress.json`
every N entities, deleted on success, resumed on retry. Returns
`{ scanned, promoted, markedUngrounded, stillFlagged, tier2Invoked }`.

## Risks / Trade-offs

- **Topical fabrication slips past Tier 1 into "Uncertain" when judge is off** → stays
  `NeedsReview`, i.e. safe (no false promote). *Mitigation:* the whole point of Tier 1 being
  escalation-only; a fabrication can only be *auto-flagged* by the judge, and can only be
  *auto-promoted* by Tier 0 field-match or the judge — never by topical similarity.
- **Judge false-positive promotes a fabrication** → the one real correctness risk. *Mitigation:*
  the judge prompt asks specifically about *field support*, not topicality; and promotion is
  additionally reason-gated. Residual risk accepted (this is strictly better than today's
  no-Tier-2 state, and never worse than a human mis-accept).
- **Tier 1 similarity floor mis-tuned** → too-low floor lets fabrications escalate (cost, not
  correctness — judge still decides); too-high floor rejects real entities to `Ungrounded` only
  when judge enabled. *Mitigation:* floor is config; auto-flag requires the judge, so a bad floor
  can't silently mass-decline without Tier 2 confirmation.
- **Entity source prose unavailable at backlog time** (no clean `dnd_blocks` neighbourhood) → Tier
  1 can't run; entity stays `Uncertain`/`NeedsReview`. Safe degradation.
- **qwen3 cost on large backlogs** → `?judge=true` is explicit and checkpointed; default is the
  fast Tier 0/1 pass.
