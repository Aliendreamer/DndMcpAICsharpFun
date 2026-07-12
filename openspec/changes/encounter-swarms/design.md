## Context

The encounter-design slice (archived `encounter-design`) ships one deterministic math core
(`EncounterMath`) shared by an `EncounterAssessor` (rate) and an `EncounterGenerator` (build), so a
built encounter is never rated differently than the same monsters passed to the assessor. Two per-user
chat tools (`rate_encounter`, `build_encounter`) and the `EncounterPanel` UI (CampaignTable + Scratch)
surface it; built monsters feed the initiative tracker as individual combatants.

The deferred v2 gap is *quantity*: the generator removes each picked candidate from the pool
(`remaining.Remove(bestCandidate)`), so it can never produce multiples; and `RateForUserAsync` maps
one string to one `MonsterRef`, so a swarm can't be typed. The `EncounterMath` count-multiplier
already keys off `monsters.Count`, so the math already handles quantity — only selection and input
need to change.

## Goals / Non-Goals

**Goals:**

- Build side produces a **boss + minions** shape (one strong anchor + multiples of a cheaper monster).
- Rate side accepts **structured `{name, quantity}`** pairs and rates them correctly.
- The **build↔rate agreement invariant** continues to hold for swarms (a built swarm rated back yields
  the same difficulty band).
- Each swarm member remains an **individual combatant** in the tracker (flat list preserved).
- Grouped display ("8× Goblin") at the chat/UI edges only.

**Non-Goals:**

- No mob/swarm combat rules (grouped HP, one turn for the pack) — each goblin is its own combatant.
- No multiple co-equal anchors ("3 dragons", "multiple gods") — single-boss + minions only this slice.
- No raised `MaxMonsters` cap and no `maxMonsters`/`variety` caller knobs (YAGNI; the honest
  "best achievable" note already covers weak-swarm-vs-high-band).
- No changes to `EncounterMath` or `EncounterAssessor` internals, no HTTP/MCP route, no migration.

## Decisions

### D1 — Flat `IReadOnlyList<MonsterRef>` stays the canonical set; quantity lives at the edges

Repeats in the flat list make the count-multiplier and the combat hand-off correct with zero engine
change, and individual combatants are what running the fight needs (Q2). Quantity is modeled only as
(a) a `MonsterQuantity(string Name, int Quantity)` **input** record for rate, and (b) a
`MonsterGrouping.Group(IReadOnlyList<MonsterRef>) → IReadOnlyList<MonsterCount(MonsterRef Monster, int Count)>`
**display** helper grouping by `Id` in first-appearance order.

*Alternative considered:* a `(MonsterRef, Quantity)` group model carried through the assessment.
Rejected — it would reshape the assessment and every consumer, then get expanded back to individual
combatants for the tracker anyway.

### D2 — Build: anchor-then-fill (boss + minions)

`EncounterGenerator.BuildAsync` keeps its greedy re-assess-after-each-add loop and overshoot guard,
but stops removing picked candidates so multiples are possible. The selection becomes two-phase:

1. **Anchor:** the first pick is the single highest-adjusted-XP candidate that does not overshoot the
   target band (identical to today's first greedy pick). Its raw XP is recorded as `anchorXp`.
2. **Fill:** subsequent rounds re-select greedily (max adjusted XP, no overshoot) from candidates with
   `m.Xp < anchorXp` — the minions — added in multiples until the target band is reached,
   `MaxMonsters = 15` is hit, or every cheaper candidate would overshoot.

**Degenerate cases fall out of the same loop:**

- Anchor alone reaches/passes the target → solo boss, no fill.
- No candidate is strictly cheaper than the anchor (e.g. all one CR) → fill re-selects the anchor tier
  itself, yielding a uniform swarm rather than dead-ending.
- Trivial target for a strong party → one weak monster (as today).

The existing overshoot/scarcity `Note` and `FullyMatched` flag are preserved verbatim.

*Alternatives considered:* (A) pure "un-remove" greedy — simplest but converges on a uniform swarm of
the single top-fitting monster, not the boss+minions shape chosen in brainstorming; (C) a grouped
`(type, quantity)` optimization solver — a big rewrite of a working engine, YAGNI.

### D3 — Rate: structured pairs expand to repeated refs

`RateForUserAsync(... IReadOnlyList<MonsterQuantity> monsters ...)` resolves each pair's name **once**
via the existing `ResolveMonsterAsync`, then repeats the `MonsterRef` `Clamp(Quantity, 1, MaxCopiesPerType=100)`
times into the flat list. `Quantity ≤ 0 → 1`. The empty-list "no monsters" case is unchanged. The
`rate_encounter` chat tool's `monsters` param reshapes from `string[]` to an array of
`{name, quantity}`; its presence/behavior tests update. Chat-only tool → no `.http`/`.insomnia`.

*Alternative considered (brainstorm Q5):* inline count parsing ("8x goblin"). User chose structured
pairs — unambiguous, no plural/format guessing.

## Risks / Trade-offs

- **[Weak swarm can't fill a high band]** → already mitigated: the greedy loop stops at
  `MaxMonsters=15` and the existing `Note` honestly reports "best achievable is Medium with 15
  monster(s)" rather than a silent under-budget encounter.
- **[Large clamp still lets 100 combatants reach the tracker]** → the build path is bounded by
  `MaxMonsters=15`; only the *rate* path allows up to 100 per type, and rate never feeds the tracker
  (it only assesses). Acceptable.
- **[Grouped display drift from flat assessment]** → `MonsterGrouping` is a pure function of the flat
  list, so the grouped echo and the assessed set can never disagree; the build↔rate integration test
  is extended to a swarm to lock the invariant end-to-end.
- **[Anchor is always the single most expensive monster]** → intended shape this slice; multiple
  co-equal anchors ("3 dragons"/"multiple gods") is explicitly deferred (Non-Goals).
