## 1. Backend

- [x] 1.1 Add `Domain/Note.cs` (Id, UserId, CampaignId, Title, Content, CreatedAt, UpdatedAt)
- [x] 1.2 Add `DbSet<Note> Notes` + config (index on CampaignId) to `AppDbContext`
- [x] 1.3 Add `Features/Campaigns/NoteRepository.cs` (GetByCampaignAsync, CreateAsync, UpdateAsync, DeleteAsync) via `IDbContextFactory<AppDbContext>`
- [x] 1.4 Register `NoteRepository` in `DatabaseExtensions`
- [x] 1.5 Extend `CampaignRepository.DeleteAsync` to delete the campaign's notes
- [x] 1.6 Generate the `AddNotes` EF migration

## 2. Tests

- [x] 2.1 `NoteRepositoryTests` (postgres collection): create+list, newest-first ordering, campaign scoping, delete, cascade-on-campaign-delete

## 3. UI

- [x] 3.1 Add a Notes section to `CampaignDetail.razor`: list (newest first), add (title + content), delete

## 4. Verify

- [x] 4.1 `dotnet build` 0/0; `dotnet test` green
- [x] 4.2 Manual: add/list/delete a note on a campaign in the UI
