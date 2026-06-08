## Why

Campaign notes can be created and deleted but not edited — a typo or a note that needs updating forces the user to delete and re-create it. The repository already supports updates (`NoteRepository.UpdateAsync`); only the UI wiring is missing.

## What Changes

- Add inline editing of an existing note's title and content in `CampaignDetail.razor`, persisting via the existing `NoteRepository.UpdateAsync`.

## Capabilities

### New Capabilities
<!-- none -->

### Modified Capabilities
- `campaign-notes`: adds a requirement for editing an existing note.

## Impact

- `Components/Pages/Campaigns/CampaignDetail.razor` (edit toggle + form, save handler)
- No repository or schema changes (`UpdateAsync` already exists)
