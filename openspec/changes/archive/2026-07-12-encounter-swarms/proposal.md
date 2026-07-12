## Why

The shipped encounter-design slice builds only *variety* encounters — the generator selects each
candidate monster exactly once, and the `rate_encounter` tool resolves one string to one monster.
A DM cannot ask for "a hobgoblin leading eight goblins" (build side) nor rate a hand-typed swarm
(rate side). The core `EncounterMath` count-multiplier already accounts for monster quantity, so
swarms are the natural next slice on top of the existing engine.

## What Changes

- **Build side — boss + minions.** `EncounterGenerator` learns to select the same monster multiple
  times: it takes the single highest-XP-under-target candidate as the *anchor* (boss), then fills
  toward the target band by re-selecting from candidates strictly cheaper than the anchor (the
  minions). Produces "1× Hobgoblin + 8× Goblin" shapes. The existing overshoot/scarcity `Note` and
  the `MaxMonsters = 15` cap are preserved.
- **Rate side — structured quantities.** `RateForUserAsync` accepts structured `{name, quantity}`
  pairs instead of bare names; each pair is resolved once and repeated `quantity` times into the
  flat monster list. Quantity ≤ 0 → 1; a per-type clamp (`MaxCopiesPerType = 100`) bounds the list.
  The `rate_encounter` chat tool's `monsters` parameter reshapes from `string[]` to `{name, quantity}`
  pairs.
- **Representation.** The assessed monster set stays a flat `IReadOnlyList<MonsterRef>` (repeats =
  quantity), so `EncounterMath`/`EncounterAssessor` need no internal change and each swarm member
  becomes its own combatant in the initiative tracker. A new presentational
  `MonsterGrouping.Group` helper collapses the flat list to `(monster, count)` for chat text and the
  `EncounterPanel` summary only.
- **Surfaces.** `build_encounter` echoes the grouped result ("A hobgoblin leading 8 goblins");
  `EncounterPanel` (CampaignTable + Scratch) renders grouped counts; the combat hand-off is
  unchanged (flat list → individual combatants). No new HTTP/MCP route, no migration.

## Capabilities

### New Capabilities

<!-- None — this extends the existing encounter-design capability. -->

### Modified Capabilities

- `encounter-design`: the generator gains quantity/swarm (anchor-then-fill) selection; the per-user
  `rate_encounter` tool accepts `{name, quantity}` pairs; the build↔rate agreement invariant now
  holds for swarms with repeated monsters.

## Impact

- **Code:** `Features/Encounters/` — `EncounterGenerator` (anchor-then-fill), `EncounterDesignService`
  (`RateForUserAsync` structured pairs), new `MonsterQuantity` input record and `MonsterGrouping`
  display helper. `Features/Chat/DndChatService.cs` — `rate_encounter` param reshape + grouped
  `build_encounter` echo. `CompanionUI/Components/EncounterPanel.razor` — grouped summary.
- **Tests:** generator anchor-then-fill / solo-boss / uniform-swarm-fallback; rate structured-pair
  expansion + clamp; chat-tool shape update; extend the real-Qdrant build↔rate integration test to a
  swarm build.
- **No** HTTP endpoint, `.http`/`.insomnia`, migration, or shared-key MCP surface change (chat-only
  tools).
