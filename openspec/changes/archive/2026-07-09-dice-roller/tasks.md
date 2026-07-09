## 1. Dice expression parser (pure, TDD)

- [ ] 1.1 Add `Features/Dice/DiceExpression.cs` — `readonly record struct DiceExpression(int Count, int Die, int Modifier, RollMode Mode)` where `enum RollMode { Normal, Advantage, Disadvantage }`; a static `TryParse(string, out DiceExpression, out string? error)` (no throw) and a `Parse` that throws `FormatException` for callers that want it. Supported dice {4,6,8,10,12,20,100}; count 1..MaxCount (const, e.g. 100); adv/dis only when Die==20 && Count==1.
- [ ] 1.2 Failing `DiceExpressionTests`: `"2d6+3"`→(2,6,+3,Normal); `"d20"`→(1,20,0,Normal); `"1d20-1"`→(1,20,-1); adv flag on d20 ok, on non-d20 or 2d20 → error; `"1d7"`→error; `"999d100"`→error (over MaxCount); garbage → error. Assert `TryParse` returns false + a non-null error and does NOT throw.
- [ ] 1.3 Implement the parser (regex or manual); make 1.2 pass; build 0/0.

## 2. RNG seam + roller (TDD)

- [ ] 2.1 Add `Features/Dice/IRandomSource.cs` — `interface IRandomSource { int Next(int minInclusive, int maxExclusive); }`; `SystemRandomSource` (wraps `Random`) for production; a test `ScriptedRandomSource`/seeded source in tests. DI-register `IRandomSource`→`SystemRandomSource` (singleton or scoped).
- [ ] 2.2 Add `Features/Dice/RollResult.cs` — `record RollResult(DiceExpression Expression, IReadOnlyList<int> Dice, IReadOnlyList<int> Kept, int Modifier, int Total, string Breakdown)`.
- [ ] 2.3 Add `Features/Dice/DiceRoller.cs` — `sealed class DiceRoller(IRandomSource rng)` with `RollResult Roll(DiceExpression e)`: roll `Count` dice in `[1,Die]` (Normal); Advantage/Disadvantage → roll two d20, keep highest/lowest (Kept = the kept single value; Dice = both rolled); `Total = sum(kept dice) + Modifier`; build the `Breakdown` string.
- [ ] 2.4 Failing `DiceRollerTests` with a scripted `IRandomSource`: `3d6` all in [1,6]; `2d6+3` with scripted [4,5] → Total 12, Dice [4,5]; adv scripted [18,7] → Kept 18, Dice [18,7]; dis scripted [18,7] → Kept 7; same seed twice → identical; breakdown strings match `"2d6+3 → [4,5]+3 = 12"` / `"d20 (adv) → [18,7] → 18"`.
- [ ] 2.5 Implement roller + breakdown formatter; make 2.4 pass; build 0/0.

## 3. Blazor DiceRoller component

- [ ] 3.1 Add `CompanionUI/Components/DiceRoller.razor` (+ code-behind if the repo uses `.razor.cs`): quick-die buttons d4/d6/d8/d10/d12/d20/d100, a count stepper, a `+/-` modifier input, an adv/dis toggle (enabled only when the selected die is d20 & count 1) OR a free-text expression field; a Roll button. On roll: build/parse a `DiceExpression` (show the parser's error on failure, no throw), call injected `DiceRoller`, render the `RollResult` (total + breakdown), and prepend it to an in-component `List<RollResult>` capped at ~10 (most recent first). Inject `DiceRoller` (and thus `IRandomSource`).
- [ ] 3.2 Embed `<DiceRoller />` on `CompanionUI/Pages/Campaigns/CampaignDetail.razor` in a sensible spot (a panel/section). Follow the existing component/markup patterns + `app.css` classes.
- [ ] 3.3 Build 0/0 (the Blazor build compiles the component + page). Confirm the component renders (no runtime binding errors) — build-verified; live/Playwright check deferred to Task 4.

## 4. Verify + review

- [ ] 4.1 Full build 0/0 + full suite green (incl. Testcontainers). Run `dotnet format --verify-no-changes` for the new files (normalize if needed).
- [ ] 4.2 Drive it (per `verify`/`run`): if the stack is up, open a campaign page, roll `2d6+3`, a d20 with advantage, and an invalid expression — confirm the result/breakdown, the recent-rolls list, and the error message. Defer honestly if no live run; the core unit tests carry correctness.
- [ ] 4.3 Whole-branch review (opus) — cross-check every ADDED requirement (parse valid/invalid, roll range, adv/dis keep, breakdown, component ephemeral-no-persist). Address findings; stop for the user's commit/archive directive.
