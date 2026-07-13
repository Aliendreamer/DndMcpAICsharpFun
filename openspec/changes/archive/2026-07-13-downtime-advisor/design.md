## Context

`rules-adjudication` (shipped) established the pattern this reuses: an ownership-free tool scopes prose
retrieval to a fixed source-book set via `RetrievalQuery.SourceBooks` (the shipped OR filter), returns
cited passages, and the persona composes under a grounding contract — no LLM call in the service. The
detailed downtime/crafting rules live in XGE (Xanathar's), now ingested (book id 5, displayName
"Xanathar's Guide to Everything"). `dnd_blocks.source_book` is the book DISPLAY NAME (verified for
prior books), so the scope keys are display names — XGE's to confirm live.

## Goals / Non-Goals

**Goals:**

- Answer downtime/crafting questions with a grounded, cited plan drawn from the XGE (+ DMG) rules.
- Reuse the shipped `ask_rules` architecture and the source-book OR filter; keep the slice small.

**Non-Goals:**

- A deterministic crafting CALCULATOR (mundane material/progress math; magic-item rarity table) —
  PARKED v2, added only if the persona's from-the-rule math proves unreliable.
- Ownership/campaign coupling (universal rules).
- A multi-category filter, migration, HTTP/MCP surface.

## Decisions

### D1 — Scope to the downtime source books via the shipped SourceBooks OR filter

`DowntimeSources.Books` = the fixed downtime source-book display-name set:
`{"Xanathar's Guide to Everything", "Dungeon Master's Guide 2014"}` (XGE detailed rules + DMG basics).
The values must equal the real `dnd_blocks.source_book` payload (DATA-INVARIANT lesson: verify the XGE
value live before trusting the scope — see the plan). `DowntimeService.PlanAsync` passes them as
`RetrievalQuery.SourceBooks` at `DowntimeSources.TopK` (~10). Excludes unrelated prose with zero new
retrieval code.

### D2 — Plan contract; persona composes

`plan_downtime` returns the scoped cited passages; its description binds the contract: compose a
downtime plan (activity, time cost, gold cost, outcome) ONLY from the returned passages, cite each,
and say the rules don't detail the activity when nothing is returned — never invent times/costs. Same
as `ask_rules`; no separate LLM call.

### D3 — Ownership-free service + tool

`DowntimeService(IRagRetrievalService rag)` with `PlanAsync(string activity, DndVersion? edition,
CancellationToken ct)` — no ownership/userId. `DowntimePlanResult(IReadOnlyList<CitedPassage>
Passages, IReadOnlyCollection<string> ScopedBooks)` (reuses `CitedPassage`). `plan_downtime(activity,
edition?)` closes over nothing user-specific; `edition` defaults to null (no edition filter, mirroring
the `ask_rules` fix). Registered in the authenticated block; exposes no `userId`/`campaignId`.

## Risks / Trade-offs

- **[XGE not ingested when code lands]** → the code proceeds on seeded-block tests; the live smoke +
  DATA-INVARIANT scope check run after XGE's blocks land (Phase 1). `Generic`/no-XGE degrades to
  honest-empty for XGE-only activities (DMG basics still answer).
- **[`DowntimeSources` strings must equal the live payload]** → the DATA-INVARIANT gate: verify the
  real XGE downtime blocks carry `source_book = "Xanathar's Guide to Everything"` via
  `GET /retrieval/search` before trusting the scope; fix the constant if it differs.
- **[Persona's crafting math is fuzzy]** → the grounding contract anchors it to the cited rule; the
  deterministic calculator (v2) is the fix if needed.
- **[Thin over `ask_rules`]** → the differentiator is the downtime-source scope + the plan framing;
  it's a sibling surface for a distinct content domain, not new logic.
