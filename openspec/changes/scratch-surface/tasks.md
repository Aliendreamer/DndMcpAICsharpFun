## 1. EncounterPanel — optional explicit party + off-campaign save guard

- [ ] 1.1 Add an optional `[Parameter] public IReadOnlyList<int>? PartyLevels { get; set; }` to `CompanionUI/Components/EncounterPanel.razor`.
- [ ] 1.2 In `BuildAsync`, pass `partyLevels: PartyLevels` to `EncounterDesignService.BuildForUserAsync(...)` instead of the hard-coded `null` (when unset it is `null`, preserving today's campaign-party behavior).
- [ ] 1.3 Wrap the "Save to log" save row in `@if (CampaignId > 0)` so it does not render off-campaign.
- [ ] 1.4 Build 0/0; run the full suite — the campaign encounter flow (`PartyLevels` unset, `CampaignId > 0`) is behavior-neutral and must stay green.

## 2. Scratch page

- [ ] 2.1 Create `CompanionUI/Pages/Scratch.razor` at `@page "/scratch"`, `[Authorize]`, `@rendermode InteractiveServer`, resolving `_userId` from the `NameIdentifier` claim (match the existing per-campaign play page).
- [ ] 2.2 Render `<DiceRollerPanel CampaignId="0" UserId="_userId" />` (ephemeral — its `CampaignId > 0` auto-log guard makes it persist nothing).
- [ ] 2.3 Render two small numeric inputs — party size (N) and level (L) — with sensible bounds (size ≥ 1, level 1–20), and compute `partyLevels = Enumerable.Repeat(level, size).ToList()`.
- [ ] 2.4 Render `<EncounterPanel CampaignId="0" UserId="_userId" PartyLevels="_partyLevels" />`, only enabling the build once size ≥ 1 (so `partyLevels` is non-empty — `BuildForUserAsync` throws on an empty party).
- [ ] 2.5 Build 0/0; full suite green.

## 3. Sidebar nav entry

- [ ] 3.1 Add a fourth `NavLink` "🎲 Scratchpad" → `/scratch` in `CompanionUI/Layout/MainLayout.razor`, alongside Chat / Campaigns / Heroes, using the same `ActiveClass`/highlight pattern.
- [ ] 3.2 Build 0/0; full suite green.

## 4. Visual verification (Playwright)

- [ ] 4.1 Screenshot `/scratch` (desktop + mobile): dice roller + encounter builder render; Scratchpad nav link is active.
- [ ] 4.2 Roll a die on `/scratch` — result/breakdown shows in the session list; confirm no campaign-log write (ephemeral).
- [ ] 4.3 Enter size + level and build an encounter — difficulty and monsters render, and there is NO "Save to log" row.
- [ ] 4.4 On a campaign table page, confirm the encounter builder is unchanged — still shows "Save to log" and still saves.
