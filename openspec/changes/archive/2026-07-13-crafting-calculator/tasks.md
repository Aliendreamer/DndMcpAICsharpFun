## 1. CraftingMath deterministic engine

- [ ] 1.1 Create `Features/Crafting/Rarity.cs` — `public enum Rarity { Common, Uncommon, Rare, VeryRare, Legendary }`.
- [ ] 1.2 Create `Features/Crafting/CraftingResults.cs` — `public record NonmagicalCraft(int MaterialsGp, double TotalWorkweeks, double PerCrafterWorkweeks, int Days)` and `public record MagicItemCraft(Rarity Rarity, int Workweeks, int GoldCostGp)`.
- [ ] 1.3 Write failing unit tests `CraftingMathTests` covering every spec scenario: plate armor `CraftNonmagical(1500)` → (750, 30.0, 150); 3 crafters → per-crafter 10.0 / days 50; fractional `CraftNonmagical(175)` → 3.5 workweeks / 18 days; `CraftNonmagical(0)` and `(-10)` throw `ArgumentOutOfRangeException`; `crafters: 0` clamps to 1; each rarity → exact `(workweeks, gold)` cell.
- [ ] 1.4 Run the tests, confirm they fail (type/method missing).
- [ ] 1.5 Create `Features/Crafting/CraftingMath.cs` — `public static class CraftingMath` with `CraftNonmagical(int marketValueGp, int crafters = 1)` (guard `marketValueGp <= 0`; clamp crafters to ≥1; materials = `marketValueGp / 2`; total workweeks = `marketValueGp / 50.0`; per-crafter = total / crafters; days = `(int)Math.Ceiling(perCrafter * 5)`) and `CraftMagicItem(Rarity rarity)` (switch table: Common (1,50), Uncommon (2,200), Rare (10,2000), VeryRare (25,20000), Legendary (50,100000)).
- [ ] 1.6 **Corpus-verify the magic-item table** against the ingested XGE before finalizing: probe `books/conversion-cache/*xge*.marker.json` / retrieval for the "Magic Item Crafting Time and Cost" table cells; if the cells surface, confirm each encoded pair matches; record the verification (or, if the cells don't survive Marker rendering, note that the values are the published XGE table encoded + cited, matching the `EncounterMath` precedent) in a code comment on `CraftMagicItem`.
- [ ] 1.7 Run the tests, confirm all pass. Run the FULL suite to confirm no regression. Commit.

## 2. calculate_crafting chat tool

- [ ] 2.1 In `Features/Chat/DndChatService.cs`, inside the authenticated session block (alongside the other ownership-free tools such as `ask_rules`/`plan_downtime`), register `calculate_crafting` with params `int? marketValue`, `string? rarity`, `int? crafters` and a description that says: supply the item's market value (nonmagical) OR its rarity (magic item), and report the returned numbers and citation exactly — do not re-derive or invent them; use `search_entities` first if you need the item's price or rarity.
- [ ] 2.2 In the delegate: guard that exactly one of `marketValue`/`rarity` is present (both/neither → return a message asking for exactly one). For `marketValue`, call `CraftingMath.CraftNonmagical(marketValue.Value, crafters ?? 1)` and shape a result DTO with a nonmagical citation ("XGE/PHB Crafting"). For `rarity`, parse it to `Rarity` (reuse a small parse helper mirroring the existing `ParseEdition`/`ParseDifficulty` style; unknown → guard message) and call `CraftMagicItem`, shaping a result DTO with a magic-item citation ("XGE Crafting Magic Items").
- [ ] 2.3 Write tool-guard unit tests (following the existing chat-tool test pattern): both supplied → guard message; neither supplied → guard message; `marketValue:1500` → nonmagical result (750/30/150) + citation; `rarity:"rare"` → magic result (10/2000) + citation; unknown rarity string → guard message.
- [ ] 2.4 Run the new tests, confirm pass. Run the FULL suite. Commit.

## 3. Live smoke + finish

- [ ] 3.1 Live smoke via the running host/chat: ask the calculator to price plate armor (expect 750 gp materials / 30 workweeks / 150 days — contrast the v1 downtime smoke's wrong "30 days / 1,200 gp") and a rare magic item (expect 10 workweeks / 2,000 gp). Confirm the tool is invoked and the numbers are exact. (Single-param-per-branch tool → should be reliable under qwen3, unlike the 4-param `prep_session`.)
- [ ] 3.2 Update `.claude/skills/dev-flow/SKILL.md` if the smoke surfaces a new lesson (e.g. deterministic-math tools beat LLM arithmetic for numeric answers).
