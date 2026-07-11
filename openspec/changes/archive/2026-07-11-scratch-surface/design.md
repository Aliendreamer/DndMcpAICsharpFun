## Context

The table-play tools are campaign-scoped. `DiceRollerPanel` takes a `CampaignId`/`UserId` and
auto-logs each roll only `if (CampaignId > 0)` — at `0` it is already purely ephemeral.
`EncounterPanel` builds via `EncounterDesignService.BuildForUserAsync(userId, campaignId,
partyLevels: null, …)` (party resolved from the campaign) and persists on an explicit "Save to log".
`BuildForUserAsync`'s `ResolvePartyAsync` already supports an explicit `partyLevels` override (it wins
over the campaign). The sidebar (`MainLayout.razor`) lists Chat / Campaigns / Heroes.

## Goals / Non-Goals

**Goals:** a `/scratch` page for quick off-campaign dice + encounter math; reuse the shipped panels;
zero new domain/persistence.

**Non-Goals:** no initiative tracker/combat/log on this page (campaign-scoped); no new
service/table/migration/HTTP/MCP; no mixed-level party input (size + uniform level only — YAGNI for a
scratchpad).

## Decisions

**Reuse `DiceRollerPanel` at `CampaignId = 0` — no change.** The existing `if (CampaignId > 0)` guard
already makes it ephemeral, so `<DiceRollerPanel CampaignId="0" UserId="_userId" />` needs nothing new.
*Alternative:* a separate scratch dice component — rejected; the panel already does exactly this.

**Parameterize `EncounterPanel` minimally rather than fork it.** Two behavior-preserving additions:
(a) an optional `[Parameter] IReadOnlyList<int>? PartyLevels` — passed to `BuildForUserAsync` when set,
`null` (today's campaign-party behavior) when not; (b) the save row wrapped in `@if (CampaignId > 0)`.
For the campaign table page (`PartyLevels` unset, `CampaignId > 0`) both are no-ops, so its behavior is
unchanged. *Alternative:* a dedicated `ScratchEncounterPanel` — rejected; it would duplicate the
controls/result rendering for a two-line parameterization, and keeping one encounter UI avoids drift.

**The size/level party input lives on the scratch page, not the panel.** The page owns two small
inputs (size N, level L) and computes `partyLevels = Enumerable.Repeat(L, N).ToList()`, passing it to
`EncounterPanel.PartyLevels`. This keeps the scratch-only party controls out of the shared panel and
lets the panel stay a pure "build + show for a given party." The build then runs the *same* tested
`BuildForUserAsync` explicit-party path.

**`/scratch` is a new capability; the sidebar link is a modification.** The page is a distinct
surface (`scratch-surface`). `sidebar-navigation`'s requirement enumerates the links (Chat/Campaigns/
Heroes), so adding Scratchpad is a MODIFIED delta there.

## Risks / Trade-offs

- **[The `EncounterPanel` change regresses the campaign table page]** → both additions are guarded to
  be no-ops when `PartyLevels` is unset and `CampaignId > 0`; the full suite (which exercises the
  campaign encounter flow) must stay green, and a screenshot confirms the campaign builder still shows
  Save to log.
- **[Off-campaign build hitting the campaign-ownership path]** → with an explicit `partyLevels`,
  `ResolvePartyAsync` returns it and never touches the campaign, so `CampaignId = 0` never reaches an
  ownership check. (Guard the page against a zero/empty size so `partyLevels` is non-empty before
  building — `BuildForUserAsync` throws on an empty party.)
- **[Nav count/spec drift]** → the `sidebar-navigation` MODIFIED delta updates the enumerated links +
  adds a Scratchpad-active scenario.

## Migration Plan

No schema/data migration. Steps: (1) `EncounterPanel` `PartyLevels` param + save-row guard; (2) the
`Scratch.razor` page (dice + size/level + encounter panel); (3) the Scratchpad nav link; (4) verify.
Rollback is a code revert.

## Verification

No new unit logic (reuses tested services). Build 0/0; full `dotnet test` green (EncounterPanel change
behavior-neutral for campaigns); Playwright screenshots: `/scratch` renders dice + encounter; a roll
is ephemeral (session list only); a size/level build shows difficulty/monsters with NO save row; and
the campaign table page's encounter builder still shows Save to log (unchanged).
