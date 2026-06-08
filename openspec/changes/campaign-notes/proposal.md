## Why

The companion has campaigns and heroes but no place to jot free-form notes — session recaps, NPC reminders, plot threads, loot. DMs and players keep these constantly. This adds simple, campaign-scoped notes.

## What Changes

- Add a `Note` domain entity (campaign-scoped, per user) and a `NoteRepository` (EF Core, via `IDbContextFactory<AppDbContext>`, same pattern as the other repos).
- Add a `Notes` table to `AppDbContext` via a new EF migration.
- Register `NoteRepository` in DI.
- Deleting a campaign also deletes its notes (extend `CampaignRepository.DeleteAsync`).
- Add a **Notes** section to the campaign detail page: list notes (newest first), add a note (title + content), and delete a note.

## Capabilities

### New Capabilities
- `campaign-notes`: users can create, view, and delete free-form text notes scoped to a campaign.

### Modified Capabilities
<!-- none -->

## Impact

- **New**: `Domain/Note.cs`, `Features/Campaigns/NoteRepository.cs`, a Notes migration, `DndMcpAICsharpFun.Tests/Campaign/NoteRepositoryTests.cs`.
- **Modified**: `Infrastructure/Persistence/AppDbContext.cs` (DbSet + config), `Extensions/DatabaseExtensions.cs` (DI), `Features/Campaigns/CampaignRepository.cs` (cascade delete), `Components/Pages/Campaigns/CampaignDetail.razor` (UI).
- **Out of scope**: per-hero or per-session notes, markdown rendering, sharing/permissions beyond the owning user, search.
