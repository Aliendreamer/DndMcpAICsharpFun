## Why

A DM at the table asks rules questions — "can I grapple a creature that's already prone?", "how does
cover interact with a spell attack?" — that involve one or more rules and often an *interaction* the
rules don't spell out. Generic prose search returns a mix of the actual rule and unrelated monster
stat-block prose (a grappling query surfaces Monster Manual creatures that grapple, not the Grappling
rule). This adds a focused, grounded rules-adjudication tool that retrieves the actual rules, cites
them, and frames a ruling (naming the rules it combines, flagging RAW-vs-DM-call).

## What Changes

- **`ask_rules(question, edition?)` chat tool** — a per-session chat tool (registered in the
  authenticated block, but **not ownership-gated**: rules are universal, no campaign/user data).
  Retrieves rule prose **scoped to the core rulebooks** (`{PlayerHandbook 2014, Dungeon Master's Guide
  2014}`) so monster/lore prose can't drown the ruling, at a higher `topK` so both sides of an
  interaction land in the set. Returns cited passages; the chat persona synthesizes the ruling under a
  grounding contract (name the rules combined, flag RAW-vs-DM-call, honest "the rules don't directly
  cover this" when retrieval is thin — never invent a rule). No new LLM call.
- **`RulesAdjudicationService`** — `AskAsync(question, edition, ct)` resolving a fixed `RuleSources`
  set → scoped prose retrieval (reusing the shipped multi-source-book OR filter) → cited passages.
  Mirrors `SettingLoreService` **minus ownership**, scoping by the rulebook set instead of a
  per-campaign setting.
- **`RulesRulingResult` / reused `CitedPassage`** — the grounded, model-facing result shape.

## Capabilities

### New Capabilities

- `rules-adjudication`: the fixed rule-source scope, the `RulesAdjudicationService`, and the
  ownership-free per-session grounded-cited `ask_rules` chat tool.

### Modified Capabilities

<!-- None — reuses the already-shipped rag-retrieval multi-source-book OR filter unchanged. -->

## Impact

- **Code:** new `Features/Rules/` (`RuleSources` constant, `RulesAdjudicationService`,
  `RulesRulingResult`; reuse `Features/Lore`'s `CitedPassage` or a local twin); `Features/Chat/
  DndChatService.cs` (register `ask_rules`, inject the service); DI wiring pulled into `AddDndChat`
  and registered in the `FullContainerScopeValidationTests` replica.
- **No** migration, no HTTP route, no `.http`/`.insomnia` change (chat-only tool), no shared-key MCP
  surface. Reuses the shipped `RetrievalQuery.SourceBooks` OR filter unchanged.
