## Why

Every table-play tool today lives inside a campaign — to roll a die or sanity-check an encounter's
difficulty, the DM has to open a campaign first. A global scratch surface lets them do quick,
throwaway rolls and encounter math without that ceremony.

## What Changes

- **New `/scratch` page** (a "🎲 Scratchpad" sidebar entry) hosting off-campaign **dice** and
  **encounter building** — no initiative tracker or combat (those stay campaign-scoped).
- **Ephemeral dice** — the shipped dice roller, reused with no campaign, so rolls appear in the
  session list but persist nowhere.
- **Encounter from a typed party** — the DM enters "N characters at level L"; the shipped encounter
  builder runs against that explicit party (no campaign heroes to read) and shows its difficulty
  assessment, with no "save to log" (there's no campaign to save to).

No new domain, persistence, migration, HTTP route, or MCP tool — it reuses the shipped dice and
encounter services, so no `.http` / `.insomnia` change.

## Capabilities

### New Capabilities

- `scratch-surface`: the global, non-campaign page that hosts ephemeral dice rolling and
  explicit-party encounter building, reached from the sidebar.

### Modified Capabilities

- `sidebar-navigation`: the sidebar now renders a fourth link (Scratchpad) alongside Chat, Campaigns,
  and Heroes, with the same active-route highlighting.

## Impact

- **New code**: `CompanionUI/Pages/Scratch.razor` (the page: dice + a size/level party input + the
  encounter panel).
- **Modified code**: `CompanionUI/Layout/MainLayout.razor` (the Scratchpad nav link);
  `CompanionUI/Components/EncounterPanel.razor` (an optional `PartyLevels` parameter passed to
  `BuildForUserAsync`, and the save row shown only when there is a campaign).
- **Reused unchanged**: `DiceRollerPanel` (its `CampaignId == 0` path is already ephemeral),
  `EncounterDesignService.BuildForUserAsync` (its explicit-`partyLevels` path is already shipped and
  tested).
- **No** new domain/persistence/migration, DI, or HTTP/MCP change.
- **Verification**: build 0/0, full `dotnet test` green (the `EncounterPanel` changes are behavior-
  neutral for the campaign table page), and Playwright screenshots of the scratch page (ephemeral
  roll; size/level encounter build with no save row) plus a confirm the campaign encounter builder
  still saves.
