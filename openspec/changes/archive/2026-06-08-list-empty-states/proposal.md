## Why

The Campaigns and Heroes pages already show friendly empty states, but this behavior is undocumented as a requirement, and the Chat page shows a blank thread with no guidance when there is no history. Capturing the as-built behavior and filling the one real gap gives consistent first-run UX.

## What Changes

- Document the existing empty states for the Campaigns list ("No campaigns yet…") and Heroes list ("No heroes yet…") as requirements.
- Add an empty state to the Chat page shown when there are no messages (e.g. "Ask me anything about your campaign.").

## Capabilities

### New Capabilities
- `ui-empty-states`: friendly empty-state messaging for the primary list/content views (Campaigns, Heroes, Chat).

### Modified Capabilities
<!-- none -->

## Impact

- `Components/Pages/Chat.razor` (empty-state block when no messages) — only real code change
- `Components/Pages/Campaigns/Campaigns.razor`, `Components/Pages/Heroes.razor` (already implemented; verified against spec)
