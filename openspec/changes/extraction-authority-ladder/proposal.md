## Why

The official-book decline gate treats "no 5etools match" as "not a real entity" and drops the candidate before the LLM ever runs. SCAG proved this wrong at scale: **127 real subclass candidates were declined** (`scag.declined.json` — Path of the Battlerager, Bladesinging, Storm Sorcery, …), every one a genuine heading with rules text in the book, simply because the 5etools **base-class** roster doesn't list subclasses. The gate conflates three separate questions:

1. **Does it exist?** — provable deterministically from the book (a real PDF `section_header` with body prose). No 5etools or web needed.
2. **Are the extracted fields hallucinated?** — already answered by the grounding cascade (fields must trace to the source prose).
3. **How authoritative is it (canon vs known-third-party vs the author's homebrew)?** — the *only* question that needs an external index, and it should set a **label**, not gate keep/drop.

Gating existence on question 3 discards real content. This change re-bases the pipeline on the book itself for existence + anti-hallucination, and turns 5etools/web into an **authority label**, never a drop.

## What Changes

- **Tier 1 — index the subclass roster.** `EntityNameIndex` loads the `subclass[]` array already present in every `5etools/class/class-*.json` (name + shortName → `EntityType.Subclass`). A subclass candidate then matches → `DeterministicTypeResolver` already does `Force(Subclass)` → it extracts and grounds to the 5etools subclass slug instead of declining. Small, no new data.
- **Tier 2 — official-book gate becomes book-grounded, not 5etools-gated. BREAKING (extraction behavior).** For an official book, a gated-prior candidate with no 5etools match is **no longer auto-declined**; it defers to extraction grounded by the book's own prose, and the existing grounding cascade validates the fields. A candidate is declined ONLY when it isn't entity-like / has no body (not an entity) or its fields **fail grounding** (hallucination). 5etools, when matched, still forces type + grounds to slug + enriches.
- **Tier 3 — web referee for the sourceless case.** Add a web-search (SearXNG) tier to the grounding cascade, invoked for candidates with **no authoritative corroboration** — keyless books, or official candidates with no 5etools match. It is **refute-biased** (a real authoritative-looking hit is required to confirm; a mere mention is not) and it only sets an **authority label**, never drops. Latency is acceptable (batch ingestion).
- **Authority label on every entity** (extends `dataSource`): `canon` (5etools match) · `canon-unindexed` (official book, no match) · `verified-thirdparty` (keyless, web-confirmed) · `homebrew` (keyless, web miss — kept and flagged, down-weightable in retrieval). Nothing is ever silently dropped for lack of a match.

## Capabilities

### New Capabilities

- `extraction-authority-ladder`: Re-bases entity existence + anti-hallucination on the book (deterministic heading+body existence; grounding-cascade field validation), indexes the 5etools subclass roster, adds a refute-biased web-search referee tier for sourceless candidates, and attaches an authority label (`canon`/`canon-unindexed`/`verified-thirdparty`/`homebrew`) to every entity — replacing "5etools-miss ⇒ drop" with "5etools/web ⇒ label."

### Modified Capabilities

- `authoritative-allowlist`: the requirement that declines a non-matching official candidate of a gated type is changed — a no-5etools-match official candidate is deferred to book-grounded extraction and labeled, not declined; declines are reserved for non-entity or grounding-failed candidates.

## Impact

- **Code:** `Features/Ingestion/EntityExtraction/EntityNameIndex.cs` (index `subclass[]`), `DeterministicTypeResolver.cs` (relax the official-book decline; the `Force(Subclass)` path already exists), the grounding cascade (`GroundingCascade`, `ITier1Grounding`, `IGroundingJudge` — add Tier-3 web), a new `IWebAuthorityReferee` over the existing SearXNG client, the entity envelope/`dataSource` (authority label), and the disposition/declined-records writer.
- **Config:** a toggle + timeout for the web tier (off by default until validated); refute-threshold knobs.
- **Data:** no new files (subclass data already on disk); the SearXNG service already runs in the stack.
- **Retrieval:** `dnd_entities` gains an authority label consumers can filter/down-weight on.
- **Docs:** `DndMcpAICsharpFun.http` + `dnd-mcp-api.insomnia.json` if any admin surface changes (e.g. a re-label/backlog endpoint).
- **Phasing:** T1 and T2 are shippable independently and immediately fix the SCAG-class loss; T3 (web) is the heaviest/most independent and can land after.
