## Context

The retrieval stack already has prose RAG (`RagRetrievalService` over `dnd_blocks`), typed entity
search, and fused retrieval; `search_lore` exposes prose RAG to the chat. `RetrievalQuery` already
carries a single `SourceBook` filter that `RagRetrievalService` applies as a Qdrant keyword match.
Campaigns carry a free-text `Description` but no structured setting. `SettingTags` on entities exist
but are EMPTY corpus-wide, and the ingested corpus is core rulebooks only (PHB/DMG/MM/TCE/SRD) — no
setting-book lore. Per-user chat tools that act on a campaign already exist (`rate_encounter`/
`build_encounter` via `EncounterDesignService`, ownership-checked with `CampaignRepository.GetByIdAsync
(id, userId)`), and the character-coach tools established the "return grounded/cited data, let the
persona compose" contract.

## Goals / Non-Goals

**Goals:**

- A campaign declares a **setting**; lore answers for it are drawn from that setting's source books
  (plus core rules), not generically.
- A **grounded, cited** per-campaign lore tool: answers come from scoped retrieved passages, cited;
  honest empty when the setting's sources have nothing.
- Retrieval can be **scoped to a set of source books** in one call (multi-book OR filter).
- Degrade gracefully: `Generic`/no setting → unscoped (today's behavior).

**Non-Goals:**

- No populating `SettingTags` on entities (structured setting tagging is a separate, larger effort);
  scoping is by **source book**, not setting tag.
- No lore-answer UI panel (answers go through chat); the only UI is the campaign setting `<select>`.
- No new LLM/synthesis service — the tool scopes+retrieves, the chat persona synthesizes.
- No entity-side setting scoping in this slice (prose `dnd_blocks` is the spine for lore; entities
  secondary and deferred).
- Phase 1 ingestion uses existing endpoints; no new ingestion code.

## Decisions

### D1 — Scope by SOURCE BOOK set, resolved from a named-setting catalog

A `CampaignSetting` catalog (in-code registry, like `FivetoolsSourceRegistry`) maps each setting to
its source-book keys, unioned with the core rulebooks: `Eberron → {ERLW} ∪ {PHB, DMG, MM}`. `Generic`
(the default) → no scope (all books). This is the honest mechanism given `SettingTags` are empty and
source books ARE populated and filterable. The catalog starts with Eberron + Generic and grows per
ingested setting book.

*Alternative considered (brainstorm):* per-campaign source-book multi-select (no catalog). Rejected —
the user chose a named-setting catalog for the world-flavored UX; the catalog is a thin registry.

### D2 — `Campaign.Setting` persisted + a minimal form `<select>`

Add a nullable `Setting` (the catalog key, e.g. `"Eberron"`, or null/`Generic`) to `Campaign` via an
additive EF migration; `CampaignRepository` create/update carry it. The campaign create/edit form
gains one `<select>` populated from the catalog. Storing on the campaign is the honest model (a
campaign IS set in a world) and unlocks future setting-awareness; the tool reads it rather than taking
a spoofable setting argument.

### D3 — Multi-source-book OR filter on the prose RAG path

Extend `RetrievalQuery` with a `SourceBooks` set (the existing single `SourceBook` stays for the
public endpoint's back-compat) and `RagRetrievalService` to emit a Qdrant `should`/OR condition over
that set when present. `rag-retrieval` capability's source-book filter requirement is modified to
allow a set with OR semantics. A single-element set behaves exactly like the single filter.

### D4 — `ask_setting_lore` returns grounded cited passages; persona synthesizes

`SettingLoreService.AskForUserAsync(userId, campaignId, question, ct)`: ownership-check the campaign
(`GetByIdAsync(id, userId)` → throw on foreign/missing, the SEC-08 pattern), resolve its `Setting` →
book set via the catalog, run the scoped prose RAG, and return the retrieved passages each carrying
its **citation** (source book + section/title) — or an explicit empty result. The `ask_setting_lore`
chat tool closes over the session `userId`, takes `campaignId` + `question`, and its description
binds the grounding contract: answer ONLY from the returned passages, cite each, and say the
setting's sources don't cover it when empty. No separate LLM call — synthesis is the persona's job,
grounded by the tool's scoped-cited output (same as the character-coach tools).

## Risks / Trade-offs

- **[Corpus has no setting lore until Phase 1 runs]** → Phase 1 (register + ingest-blocks ERLW) is a
  hard prerequisite for a meaningful demo; the feature ships correct-but-empty for settings whose
  books aren't ingested, and `Generic` always works. The live smoke runs only after Phase 1.
- **[A setting maps to a book NOT in the corpus]** → the OR filter simply matches nothing for that
  key; the tool returns honest-empty rather than erroring. The catalog only lists a setting once its
  book is (or is being) ingested.
- **[Scoping could be a silent no-op]** → the integration test seeds blocks from an in-set book AND an
  off-setting book and asserts the off-setting block is EXCLUDED (non-vacuity), so a broken/ignored
  filter fails the test (the behavior-change discrimination gate).
- **[New per-user chat tool = two guard tests]** → `ask_setting_lore` joins the `userId`-not-exposed
  schema filter AND the authenticated-present/unauthenticated-absent lists (a filter-then-assert
  alone is vacuous if the tool isn't registered).
- **[`Setting` as a free string vs enum]** → stored as the catalog key string (nullable) so adding a
  setting is a registry edit, not a schema/enum migration; unknown/legacy values resolve to `Generic`
  (unscoped), never an error.
