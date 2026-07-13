## Why

The shipped `plan_downtime` grounds the crafting *rules* but leaves the arithmetic to the LLM, which
got it wrong in the live smoke ("30 days / 1,200 gp" for plate armor — right number, wrong unit and
wrong gold; the correct answer is 750 gp materials / 30 workweeks). This adds a deterministic crafting
calculator (an `EncounterMath`-style pure math engine) so times and costs are computed exactly from
the encoded, rule-grounded formulas — not the LLM's fuzzy math.

## What Changes

- **`CraftingMath`** — a pure, static, deterministic engine (like `EncounterMath`):
  - **Nonmagical** `CraftNonmagical(marketValueGp, crafters)` → materials = ½ market value; total
    workweeks = market value ÷ 50 (the XGE downtime formula, corpus-verified); per-crafter workweeks =
    total ÷ crafters; days = workweeks × 5.
  - **Magic item** `CraftMagicItem(rarity)` → the XGE "Magic Item Crafting Time and Cost" table
    (rarity → workweeks + gold cost), values verified against the ingested XGE.
- **`calculate_crafting(marketValue?, rarity?, crafters?)` chat tool** — per-session, **not
  ownership-gated**. The LLM supplies the item's market value (nonmagical) or rarity (magic item); the
  tool returns the deterministic result and a fixed XGE/PHB citation. Contract: report the calculator's
  exact numbers and cite the rule — never re-derive the math or invent numbers. Exactly one of
  `marketValue`/`rarity` is expected.
- **Result records** for nonmagical and magic-item crafting (materials/gold cost, workweeks, days,
  prerequisites note).

## Capabilities

### New Capabilities

- `crafting-calculator`: the deterministic `CraftingMath` (nonmagical + magic-item), and the
  ownership-free `calculate_crafting` chat tool.

### Modified Capabilities

<!-- None. Complements the shipped downtime-advisor (retrieval) with deterministic math. -->

## Impact

- **Code:** new `Features/Crafting/` (`CraftingMath`, `Rarity` enum, result records). `Features/Chat/
  DndChatService.cs` — register `calculate_crafting`; the math is static so no service/DI is required
  (a thin `CraftingService` wrapper only if needed for the tool; otherwise call `CraftingMath`
  directly).
- **No** migration, HTTP route, `.http`/`.insomnia`, shared-key MCP, or new retrieval. Grounding: the
  nonmagical formula is corpus-verified; the magic-item table is the published XGE table (encoded +
  cited), verified against the ingested XGE during implementation.
