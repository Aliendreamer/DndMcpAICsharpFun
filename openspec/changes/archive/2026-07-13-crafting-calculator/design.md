## Context

`EncounterMath` (shipped) is the model: a pure, static, table-driven math engine grounded in the
published DMG tables (CR→XP, thresholds), unit-tested with no infrastructure. This applies the same
pattern to crafting. The nonmagical crafting formula was corpus-verified live: XGE "divide its gold
piece cost by 50" workweeks + "raw materials worth half"; the magic-item table exists in XGE but its
cells don't survive Marker's table rendering, so it is encoded from the published XGE table and
verified during implementation (below).

## Goals / Non-Goals

**Goals:**

- Exact, deterministic crafting time/cost — fixing the LLM's fuzzy arithmetic (the v2 rationale).
- Grounded in the cited rule: the nonmagical formula corpus-verified; the magic-item table the
  published XGE values.
- Reuse the `EncounterMath` pure-static pattern; small and unit-testable with no infrastructure.

**Non-Goals:**

- Item price/rarity LOOKUP — the LLM supplies `marketValue`/`rarity` (the calculator is pure math,
  like `EncounterMath` takes CR/party, not a monster lookup).
- Retrieval (the `plan_downtime` advisor covers the prose rules; this is the math).
- Ownership/campaign coupling, migration, HTTP/MCP surface.

## Decisions

### D1 — `CraftingMath` pure static engine (mirrors `EncounterMath`)

`public static class CraftingMath`:

- `NonmagicalCraft CraftNonmagical(int marketValueGp, int crafters = 1)` → `MaterialsGp =
  marketValueGp / 2`; `TotalWorkweeks = marketValueGp / 50.0`; `PerCrafterWorkweeks = TotalWorkweeks /
  max(1, crafters)`; `Days = ceil(PerCrafterWorkweeks * 5)`. Guards: `marketValueGp <= 0` →
  ArgumentOutOfRange; `crafters` clamped to ≥ 1.
- `MagicItemCraft CraftMagicItem(Rarity rarity)` → the XGE table:
  Common `(1, 50)`, Uncommon `(2, 200)`, Rare `(10, 2000)`, VeryRare `(25, 20000)`, Legendary
  `(50, 100000)` — `(workweeks, goldCostGp)`. `Rarity` enum {Common, Uncommon, Rare, VeryRare,
  Legendary}. (Verified vs the ingested XGE in the plan; these are the stable published values.)

Records: `NonmagicalCraft(int MaterialsGp, double TotalWorkweeks, double PerCrafterWorkweeks, int Days)`;
`MagicItemCraft(Rarity Rarity, int Workweeks, int GoldCostGp)`.

### D2 — `calculate_crafting` tool; math + fixed citation

`calculate_crafting(marketValue?, rarity?, crafters?)` — ownership-free, authenticated block. Exactly
one of `marketValue`/`rarity` drives the branch (nonmagical vs magic item); if both/neither, return a
clear "supply a market value OR a rarity" result. Returns the deterministic record plus a fixed
citation reference (nonmagical → "PHB/XGE Crafting"; magic → "XGE Crafting Magic Items"). The
description binds: report the calculator's EXACT numbers and cite the rule; never re-derive or invent.

### D3 — No service/DI unless the tool needs it

`CraftingMath` is static, so the tool delegate can call it directly (no `CraftingService`/DI). A thin
result-DTO shaping in the delegate is fine. This keeps the slice to the math + the tool registration
(mirrors how `build_encounter`/`rate_encounter` ultimately rest on the static `EncounterMath`).

## Risks / Trade-offs

- **[Magic-item table not corpus-retrievable]** → encoded from the published XGE table (like
  `EncounterMath`'s DMG tables), cited to XGE; the plan attempts a best-effort verification against the
  ingested XGE conversion and flags any mismatch. Stable published values, low risk.
- **[LLM supplies a wrong market value/rarity]** → garbage-in; the calculator is correct for its input,
  and the tool description tells the LLM to look up the item's price/rarity (via `search_entities`)
  first. The math being exact is the win over the LLM doing it inline.
- **[Two nonmagical systems (PHB 5 gp/day vs XGE value/50 workweeks)]** → the calculator uses the XGE
  downtime formula (the standard downtime crafting); the PHB slow rate is not encoded (YAGNI; noted in
  the citation).
- **[Rounding]** → workweeks kept as a double (e.g. 1500/50 = 30.0; an odd value like 175/50 = 3.5
  workweeks is meaningful); `Days = ceil(workweeks*5)`; materials integer (÷2). Tested at the plate
  (1500) and an odd-value case.
