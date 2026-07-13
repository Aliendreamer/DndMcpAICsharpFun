# crafting-calculator Specification

## Purpose
TBD - created by archiving change crafting-calculator. Update Purpose after archive.
## Requirements
### Requirement: Deterministic nonmagical crafting math

`CraftingMath.CraftNonmagical(int marketValueGp, int crafters = 1)` SHALL compute nonmagical crafting
cost and time deterministically from the XGE downtime crafting formula: materials cost = ½ market
value, total workweeks = market value ÷ 50, per-crafter workweeks = total ÷ crafters, days = per-crafter
workweeks × 5 (rounded up). It MUST NOT delegate the arithmetic to an LLM.

#### Scenario: Plate armor (the smoke's failing case)

- **WHEN** `CraftNonmagical(1500)` is called
- **THEN** materials cost is `750` gp, total workweeks is `30.0`, and days is `150`

#### Scenario: Multiple crafters divide the time

- **WHEN** `CraftNonmagical(1500, crafters: 3)` is called
- **THEN** total workweeks is `30.0`, per-crafter workweeks is `10.0`, and days is `50`
- **AND** materials cost is unchanged at `750` gp

#### Scenario: Fractional workweeks are preserved

- **WHEN** `CraftNonmagical(175)` is called
- **THEN** total workweeks is `3.5` and days is `18` (ceil of 17.5)

#### Scenario: Non-positive market value is rejected

- **WHEN** `CraftNonmagical(0)` or `CraftNonmagical(-10)` is called
- **THEN** it throws `ArgumentOutOfRangeException`

#### Scenario: Crafters below one is clamped

- **WHEN** `CraftNonmagical(1500, crafters: 0)` is called
- **THEN** it behaves as `crafters: 1` (per-crafter workweeks `30.0`, days `150`)

### Requirement: Deterministic magic-item crafting table

`CraftingMath.CraftMagicItem(Rarity rarity)` SHALL return the XGE "Magic Item Crafting Time and Cost"
values for the given rarity — a fixed table of workweeks and gold cost — deterministically, without an
LLM. The encoded values MUST match the ingested XGE (verified during implementation).

#### Scenario: Each rarity maps to its XGE table cell

- **WHEN** `CraftMagicItem(rarity)` is called for each rarity
- **THEN** it returns: Common `(1 workweek, 50 gp)`, Uncommon `(2 workweeks, 200 gp)`,
  Rare `(10 workweeks, 2000 gp)`, VeryRare `(25 workweeks, 20000 gp)`,
  Legendary `(50 workweeks, 100000 gp)`

### Requirement: Ownership-free crafting chat tool

The chat surface SHALL expose a `calculate_crafting(marketValue?, rarity?, crafters?)` tool, registered
in the authenticated session block but NOT gated on campaign ownership (it takes no userId/campaignId).
It branches on which of `marketValue`/`rarity` is supplied, returns the deterministic `CraftingMath`
result plus a fixed rule citation, and its description instructs the model to report the exact numbers
and cite the rule rather than re-deriving or inventing them.

#### Scenario: Market value drives the nonmagical branch

- **WHEN** the tool is invoked with `marketValue: 1500` (and no rarity)
- **THEN** it returns the `CraftNonmagical(1500)` result (750 gp materials, 30 workweeks, 150 days) and
  a nonmagical crafting citation

#### Scenario: Rarity drives the magic-item branch

- **WHEN** the tool is invoked with `rarity: "rare"` (and no marketValue)
- **THEN** it returns the `CraftMagicItem(Rare)` result (10 workweeks, 2000 gp) and a magic-item
  crafting citation

#### Scenario: Ambiguous or empty input is guarded

- **WHEN** the tool is invoked with both `marketValue` and `rarity`, or with neither
- **THEN** it returns a clear message asking for exactly one of a market value or a rarity, and does not
  fabricate a result

