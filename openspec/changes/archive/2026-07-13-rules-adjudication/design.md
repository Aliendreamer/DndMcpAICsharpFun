## Context

The retrieval stack has prose RAG (`RagRetrievalService` over `dnd_blocks`) and, as of
setting-aware-lore, a **multi-source-book OR filter** (`RetrievalQuery.SourceBooks` → Qdrant `should`).
The setting-aware slice established the pattern this reuses: a chat tool scopes retrieval and returns
cited passages; the persona synthesizes under a grounding contract; no new LLM call. `Features/Lore`
already has `CitedPassage(Text, SourceBook, Section, Score)`. Live corpus probe (2026-07-13): rule
prose for grappling/prone/cover IS retrievable from PHB, but the same queries also surface Monster
Manual stat-block prose — so scoping to the core rulebooks is the discriminating value.

## Goals / Non-Goals

**Goals:**

- Answer a rules question with a **grounded, cited ruling** drawn from the actual rules, not monster
  prose.
- Frame it as *adjudication*: name the rules combined, flag RAW-vs-DM-call, honest-empty when thin.
- Reuse the shipped source-book OR filter and the setting-aware architecture; keep the slice small.

**Non-Goals:**

- No multi-hop rule decomposition (identify-and-retrieve each interacting rule separately) — v2.
- No new multi-category filter (`{Rule, Combat, Condition, Adventuring}`) — v2 refinement if
  source-book scoping proves too noisy.
- No campaign/character-specific rulings (this is universal rules, not "does *my* character…").
- No ownership gating (rules are universal — no campaignId/userId).
- No migration, HTTP route, `.http`/`.insomnia`, or shared-key MCP surface.

## Decisions

### D1 — Scope to the core rulebooks via the shipped SourceBooks OR filter

`RuleSources` is a fixed set of the core rulebooks' real `dnd_blocks.source_book` display-name values
(`{"PlayerHandbook 2014", "Dungeon Master's Guide 2014"}` — the DATA-INVARIANT lesson: the strings
must equal the live payload; verified/finalized against the corpus in the plan). The service passes
them as `RetrievalQuery.SourceBooks` (the shipped OR filter) with a higher `TopK` (~10) so both sides
of an interaction land. This excludes the Monster Manual noise the probe exposed with ZERO new
retrieval code.

*Alternative considered:* a multi-category OR filter scoping to `{Rule, Combat, Condition,
Adventuring}` (more precise — excludes spell/class prose within PHB/DMG too). Deferred to v2: it needs
a new filter and the category tagging's reliability is unverified; source-book scoping already removes
the dominant (monster) noise with shipped code.

### D2 — Ruling contract; persona synthesizes

`ask_rules` returns the scoped cited rule passages; its description binds the contract: compose the
ruling ONLY from the returned passages, **name each rule combined and cite it**, **flag RAW-vs-DM-call**
where the rules don't explicitly resolve the interaction, and say **"the rules don't directly cover
this"** when no relevant passages are returned — never invent a rule. Same as `ask_setting_lore`; no
separate LLM call.

### D3 — Ownership-free service + tool

`RulesAdjudicationService.AskAsync(string question, DndVersion? edition, CancellationToken ct)` — no
`CampaignRepository`, no userId. Resolve `RuleSources` → `RetrievalQuery(question, Version: edition,
SourceBooks: RuleSources, TopK: RuleTopK)` → `rag.SearchAsync` → project to `CitedPassage` (reuse
`Features/Lore`'s record) → `RulesRulingResult(IReadOnlyList<CitedPassage> Passages,
IReadOnlyList<string> ScopedBooks)`. `ask_rules(question, edition?)` closes over nothing user-specific;
`edition` defaults to null (no edition filter, mirroring the `ask_setting_lore` honest-empty fix).

## Risks / Trade-offs

- **[One-sided retrieval misses half an interaction]** → higher `TopK` (~10) makes both Grappling and
  Prone likely to land; the contract makes the persona NAME the rules it used and flag when it can't
  find the other side, rather than confidently half-answering. Multi-hop (v2) is the real fix.
- **[Source-book scoping still includes spell/class prose from PHB/DMG]** → acceptable; it removes the
  dominant monster noise, and semantic ranking + topK surface the rule. Category-scoping (v2) tightens
  it if needed.
- **[`RuleSources` strings must equal the live payload]** → the DATA-INVARIANT gate: the plan verifies
  the actual `source_book` of real rule blocks (Grappling/Prone/Cover) before trusting the scope — a
  self-seeded test passes with the expected value even if prod differs.
- **[Thin net-new value vs `search_lore`]** → the differentiator is the rulebook scoping + the
  adjudication contract; if it reads as "search_lore with a filter," v2 (multi-hop / category) is where
  the reasoning deepens. Ship the honest small slice first.
