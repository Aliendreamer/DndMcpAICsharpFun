## Why

A DM/player asks "my ranger wants to craft plate armor — how long and how much?" or "what can the
party do during three weeks of downtime?" The detailed downtime and crafting rules live in Xanathar's
Guide to Everything (XGE), now being ingested. This adds a grounded downtime advisor that scopes
retrieval to the XGE (+ DMG basic) downtime rules and returns cited passages the persona composes into
a plan — times and costs come from the rules, never invented.

## What Changes

- **`plan_downtime(activity, edition?)` chat tool** — per-session, **not ownership-gated** (universal
  rules, no campaign/user data). Scopes prose retrieval to the downtime source books
  (`{Xanathar's Guide to Everything, Dungeon Master's Guide 2014}`) so unrelated prose can't drown the
  plan, at a higher `topK`. Returns cited passages; the persona composes a downtime plan (activity,
  time cost, gold cost, outcome) strictly from them, citing each, and says the rules don't detail it
  when nothing is returned — never inventing times/costs.
- **`DowntimeService.PlanAsync(activity, edition, ct)`** — scope via the shipped
  `RetrievalQuery.SourceBooks` OR filter → cited passages. Mirrors `RulesAdjudicationService` minus the
  rulebook scope; no LLM call.
- **`DowntimeSources` / `DowntimePlanResult`** — the fixed downtime source-book set and the
  grounded result (reuses `Features/Lore`'s `CitedPassage`).

## Capabilities

### New Capabilities

- `downtime-advisor`: the downtime source-book scope, the `DowntimeService`, and the ownership-free
  `plan_downtime` chat tool.

### Modified Capabilities

<!-- None. Reuses the shipped rag-retrieval multi-source-book OR filter unchanged. -->

## Impact

- **Data (Phase 1, prerequisite):** register + ingest-blocks the XGE PDF (in progress — book id 5).
- **Code:** new `Features/Downtime/` (`DowntimeSources`, `DowntimeService`, `DowntimePlanResult`).
  `Features/Chat/DndChatService.cs` — register `plan_downtime`, inject the service; DI pulled into
  `AddDndChat` + validated by `FullContainerScopeValidationTests`.
- **No** migration, HTTP route, `.http`/`.insomnia`, or shared-key MCP change (chat-only tool). The
  deterministic crafting *calculator* is a PARKED v2 (not this slice).
