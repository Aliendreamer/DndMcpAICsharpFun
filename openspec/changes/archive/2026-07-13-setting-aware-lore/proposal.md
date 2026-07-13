## Why

A campaign is set in a *world* — Eberron, the Sword Coast, Ravenloft — but the companion answers
lore questions generically, mixing lore from every setting in the corpus. A DM asking "what are the
Dragonmarked Houses?" for an Eberron campaign wants the answer drawn from Eberron's sources, not
whatever the vector search surfaces across all books. This adds a per-campaign setting and a
grounded, cited lore tool that scopes retrieval to that setting's source books.

## What Changes

- **Setting catalog.** A small in-code registry maps each named setting to its source-book key(s),
  always unioned with the core rulebooks (`Eberron → {ERLW} ∪ {PHB, DMG, MM}`). A `Generic`/none
  default scopes to everything (behaves like today's unscoped search). Starts with Eberron; grows as
  setting books are ingested.
- **`Campaign.Setting`.** A nullable structured setting on `Campaign` (EF migration, additive) plus a
  small setting `<select>` on the campaign create/edit form. This is the only UI in the slice.
- **Multi-source-book scoped retrieval.** Extend the prose RAG filter from a single `SourceBook`
  exact-match to a **set** (Qdrant OR/`should` over the setting's book keys), so retrieval can be
  scoped to a setting's books at once.
- **`ask_setting_lore(campaignId, question)` chat tool.** Ownership-gated (closes over the session
  user, verifies campaign ownership). Resolves the campaign's setting → source-book set → scoped
  prose retrieval → returns the retrieved **cited passages** (source book + section). The chat
  persona synthesizes the answer *from those passages, citing them*, and says "not in this setting's
  sources" when empty — no free-loring. No new LLM call: the tool scopes+retrieves; the persona
  synthesizes under a grounding contract (same architecture as the character-coach tools).
- **Phase 1 (prerequisite, existing endpoints — not code).** Ingest one setting book's prose:
  `POST /admin/books/register` (`fivetoolsSourceKey=ERLW`) → `POST /admin/books/{id}/ingest-blocks`.
  Blocks only — narrative setting lore lives there; full entity extraction is not needed for lore Q&A.

## Capabilities

### New Capabilities

- `setting-aware-lore`: the setting catalog, `Campaign.Setting`, and the ownership-gated per-campaign
  grounded-cited lore tool that scopes retrieval to the campaign's setting source books.

### Modified Capabilities

- `rag-retrieval`: the block-search source-book filter is extended to accept a **set** of source
  books (OR semantics), so a query can be scoped to a setting's books in one call.

## Impact

- **Code:** new `Features/Lore/` (setting catalog, `SettingLoreService`, the chat tool wiring);
  `Domain/Campaign.cs` + `CampaignRepository` + EF migration (`Setting` column); `Features/Chat/
  DndChatService.cs` (register `ask_setting_lore`); `Features/Retrieval/RetrievalQuery.cs` +
  `RagRetrievalService.cs` (multi-source-book OR filter); a setting `<select>` on the campaign form.
- **Data (Phase 1):** register + ingest-blocks the ERLW PDF (existing admin endpoints).
- **Migration:** additive `Campaign.Setting` column. **No** `.http`/`.insomnia` change (the tool is a
  per-user chat tool; campaign CRUD is Blazor server-side, not an HTTP route).
