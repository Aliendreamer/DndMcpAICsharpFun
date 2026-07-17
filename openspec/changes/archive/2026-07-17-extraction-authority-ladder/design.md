## Context

Entity extraction gates official-book candidates through `DeterministicTypeResolver`: a gated-prior candidate (Spell/Monster/Class/Race/Background/Feat/Condition/God) with no 5etools match and no complete stat block is **declined** (`no_5etools_match`) before any LLM call. That gate does two jobs at once:

1. **Anti-fabrication** — its stated purpose (the extraction-honesty work): don't let qwen invent canon entities.
2. **Noise filtering** — incidentally, it drops gated-typed *chapter-body headings that aren't entities* ("Ability Score Increase" with a Race prior, "Divine Domain" section intros).

SCAG exposed the cost: **127 real subclasses were declined** because 5etools indexes subclasses under the class files' `subclass[]` array, which `EntityNameIndex` never loads — so "Path of the Battlerager" is measured against the 12 base classes and fails. Meanwhile keyless/homebrew books already fall through (extract) with *no* authority check at all.

The fix separates three conflated questions — **existence** (the book: a real `section_header` + body), **field truth** (the grounding cascade: fields ⊆ prose), **authority** (5etools/web: canon vs known vs homebrew) — and makes only the third a *label*.

## Goals / Non-Goals

**Goals:**

- Stop dropping real book entities for lack of a 5etools match (recover SCAG-class subclasses).
- Keep existence deterministic (book heading+body) and keep the anti-hallucination guarantee (grounding cascade), independent of any external index.
- Preserve noise filtering when the 5etools decline is relaxed.
- Attach an authority label to every entity; never silently drop for a match miss.
- Ship in safe increments (T1 alone already fixes most of SCAG).

**Non-Goals:**

- Changing the grounding cascade's *field-validation* semantics (Tier 0/1/2 stay as-is; the web tier is an **authority** axis, not a 4th grounding tier).
- Re-typing or hand-authoring subclasses (T1 makes them match; no manual data).
- Making the web tier a hard dependency — it is opt-in/toggled, off until validated.
- Retroactively re-labeling the existing corpus (a backlog re-label endpoint is a possible follow-up, noted not built).

## Decisions

**D1 — Index the subclass roster (T1).** `EntityNameIndex` adds one load of the `subclass[]` array from `5etools/class/class-*.json`, indexing each subclass `name` **and** `shortName` → `EntityType.Subclass`. A subclass candidate then matches; `DeterministicTypeResolver` line ~72 already returns `Force(Subclass)` on any match, so it extracts and grounds to the subclass slug via the existing `FivetoolsSubclassMapper`. *Alternative rejected:* pattern-detecting subclass headers ("Path of…/Way of…") — brittle and ungrounded vs. an exact 5etools match. This alone recovers most of SCAG with **zero** noise risk (noise headings don't match the subclass roster either).

**D2 — Replace the 5etools-existence *proxy* with a book-derived `IsRealEntity` predicate (T2), applied to BOTH official and keyless books, do NOT just delete the decline.** The predicate is a **structural signature only**: the existing complete-stat-block / magic-item signatures PLUS a new **subclass-feature-progression** signature (≥2 level-gated grants, e.g. "Starting at Nth level…", "At Nth level…").

> **REVISED after live SCAG validation (2026-07-17).** The predicate originally also had a prose-OR branch — `OR (entity-like name AND substantial, non-tabular body)` — to admit prose gated types (Background, Feat, Race, God). The live re-extract proved it a **net regression**: SCAG went 62 → 108 entities, but the extra were **junk** — `Class` 8→38 and `Race` 2→13 (class *features* and race *lore* sub-sections have entity-like names + substantial bodies, so they passed and extracted as low-value Class/Race entities; `declined` collapsed 245→25). Crucially, the prose branch added **no real value**: the genuine prose entities (gods, backgrounds) already come in via 5etools matches, so dropping it loses nothing real on official books while removing the junk. Keyless *prose* entities that lack a 5etools match are therefore NOT admitted by T2 — they are deferred to T3's web-vouching (a cleaner separation: structural entities are self-evident from the book; prose homebrew entities need an external referee). The predicate fires in `DeterministicTypeResolver` on the gated-prior + no-5etools-match branch:
- **Official book:** `IsRealEntity` → `Defer` (admit → grounded extraction → label `canon-unindexed`); else → `Decline` (noise, as today).
- **Keyless book:** `IsRealEntity` → `Defer` (extract, as today); else → `Decline` — NEW: keyless books currently fall through and extract *everything* (no noise filter at all), so the predicate gives them the filtering official books already had.

Existence is thus proven by the *book's own structure/prose*, not by 5etools; extracted fields are still validated by the grounding cascade (a candidate that extracts but whose fields fail grounding is rejected). **Bonus:** SCAG's empty base-class `Class` shells (base-class headers with no signature and no substantial body) fail the predicate and are dropped. *Alternatives rejected:* (a) blanket deferral — re-admits chapter-body noise; (b) structural-signature-ONLY — wrongly drops real prose entities (backgrounds/feats/races/gods); (c) type-specific admission tests per gated type — more precise but materially more work, and the grounding cascade already backstops the fields.

**D3 — Web referee is an authority axis, not a grounding tier (T3).** The grounding cascade answers "are these fields true to the prose" and is unchanged. Authority — "is this a real/canon thing or the author's invention" — is a separate determination: 5etools match → `canon`; official + no match → `canon-unindexed`; keyless → **web referee**. The referee (`IWebAuthorityReferee` over the existing SearXNG client) is **refute-biased**: it confirms only on a strong authoritative-looking hit, and a miss downgrades to `homebrew` — it **never drops**. It runs only where there is no authoritative corroboration (keyless books, or official no-match residual), so cost is bounded; it is toggled off by default. Its value is being an *independent* second opinion where Tier 2 is qwen judging qwen.

**D4 — Authority label on the entity (extends `dataSource`).** Every emitted entity carries one of `canon` / `canon-unindexed` / `verified-thirdparty` / `homebrew`. `dnd_entities` payload gains the label so retrieval can filter or down-weight (e.g. prefer canon, caveat homebrew). Declined records (`.declined.json`) remain for true non-entities only.

## Risks / Trade-offs

- **Relaxing the decline re-admits noise** → Mitigation: D2's entity-signature test is the replacement filter; a no-signature candidate is still declined. Validate against SCAG (subclasses in, "Ability Score Increase"-class noise still out) before enabling broadly.
- **Web confirms junk** ("some page mentions it") → Mitigation: refute-bias — require an authoritative-looking hit; default to `homebrew` on doubt; a wrong label is recoverable (it never drops or fabricates).
- **Web latency / flakiness / rate limits** → Mitigation: opt-in toggle, per-call timeout, cache by normalized name, and it only runs on the sourceless residual, not every candidate.
- **qwen-judges-qwen at Tier 2** stays for grounding → the web referee is the independent cross-check specifically for the authority axis; grounding self-judging is a separate, pre-existing limitation not solved here.
- **Label schema churn** → Mitigation: additive `dataSource`/authority field; existing `5etools-backfill`/`manual` values unaffected.

## Migration Plan

Phased, each independently valuable and shippable:
1. **T1 (subclass index)** — smallest, safest; re-extract SCAG to confirm subclasses ground as `canon`.
2. **T2 (entity-signature gate)** — behind the same official path; validate SCAG noise stays out.
3. **T3 (web referee + labels)** — toggled off by default; enable after refute-bias tuning on a keyless book (e.g. EEPC once ingested).

Rollback = revert; no data migration. Re-extraction is `POST /admin/books/{id}/extract-entities?force=true` per book.

## Open Questions

- Exact **subclass-feature-progression signature** definition (how to recognize a subclass writeup from prose/structure without a stat block) — refine during T2, informed by the MTF review.
- Web referee **query shape + "authoritative hit" criterion** (which domains/signals count) and the confirm/refute threshold — tune in T3.
- Where the **authority label** lives on the entity envelope vs `dataSource` string, and whether retrieval should hard-filter or only down-weight `homebrew`.
- Whether to add a **re-label backlog endpoint** for the existing corpus (follow-up, not this change).
