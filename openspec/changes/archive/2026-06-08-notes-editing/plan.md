# Campaign Note Editing — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the user edit an existing campaign note's title and content inline on `CampaignDetail.razor`, persisting via the existing `NoteRepository.UpdateAsync`.

**Architecture:** Pure UI wiring — the repository already exposes `UpdateAsync(id, campaignId, title, content)`. Add per-note edit state to `CampaignDetail.razor`, swap a note card into an editable form on "Edit", and save through the existing repo method.

**Tech Stack:** Blazor Server, EF Core (Npgsql). No schema or repository changes.

---

## File Structure

- `Components/Pages/Campaigns/CampaignDetail.razor` — edit state, edit form, save/cancel handlers (only file changed)

`NoteRepository.UpdateAsync` already exists and is covered by `DndMcpAICsharpFun.Tests/Campaign/NoteRepositoryTests.cs`; no repo test changes needed for this UI-only change.

---

### Task 1: Edit state + handlers

**Files:**
- Modify: `Components/Pages/Campaigns/CampaignDetail.razor`

- [ ] **Step 1: Add edit state fields**

In `@code`, below the existing note fields (`_addNoteError`):

```csharp
    private long? _editingNoteId;
    private string _editTitle = "";
    private string _editContent = "";
    private string _editNoteError = "";
```

- [ ] **Step 2: Add begin/cancel/save handlers**

Add below `DeleteNoteAsync`:

```csharp
    private void BeginEditNote(Note note)
    {
        _editingNoteId = note.Id;
        _editTitle = note.Title;
        _editContent = note.Content;
        _editNoteError = "";
    }

    private void CancelEditNote()
    {
        _editingNoteId = null;
        _editTitle = "";
        _editContent = "";
        _editNoteError = "";
    }

    private async Task SaveNoteEditAsync(long noteId)
    {
        if (string.IsNullOrWhiteSpace(_editTitle))
        {
            _editNoteError = "Title is required.";
            return;
        }
        await NoteRepo.UpdateAsync(noteId, Id, _editTitle.Trim(), _editContent.Trim());
        _notes = await NoteRepo.GetByCampaignAsync(Id);
        CancelEditNote();
    }
```

- [ ] **Step 3: Build (verifies handlers compile against repo signature)**

Run: `dotnet build`
Expected: succeeds (confirms `UpdateAsync(long, long, string, string)` matches the call).

---

### Task 2: Edit form in the note card

**Files:**
- Modify: `Components/Pages/Campaigns/CampaignDetail.razor`

- [ ] **Step 1: Swap the card into an edit form when editing**

Replace the existing note-card body (the `@foreach (var note in _notes)` card block, lines ~64–73) so each card conditionally renders the edit form:

```razor
            @foreach (var note in _notes)
            {
                <div class="note-card">
                    @if (_editingNoteId == note.Id)
                    {
                        <input @bind="_editTitle" placeholder="Title" />
                        <textarea @bind="_editContent" placeholder="Write a note..." rows="3"></textarea>
                        @if (!string.IsNullOrEmpty(_editNoteError))
                        {
                            <p class="error">@_editNoteError</p>
                        }
                        <button @onclick="() => SaveNoteEditAsync(note.Id)">Save</button>
                        <button @onclick="CancelEditNote">Cancel</button>
                    }
                    else
                    {
                        <div class="note-head">
                            <strong>@note.Title</strong>
                            <span class="note-actions">
                                <button class="note-edit" title="Edit" @onclick="() => BeginEditNote(note)">✎</button>
                                <button class="note-del" title="Delete" @onclick="() => DeleteNoteAsync(note.Id)">✕</button>
                            </span>
                        </div>
                        <p class="note-body">@note.Content</p>
                        <span class="note-time">@note.UpdatedAt.ToString("yyyy-MM-dd HH:mm")</span>
                    }
                </div>
            }
```

- [ ] **Step 2: Build**

Run: `dotnet build`
Expected: succeeds, zero warnings.

- [ ] **Step 3: Commit**

```bash
git add Components/Pages/Campaigns/CampaignDetail.razor
git commit -m "feat(notes): inline edit of campaign notes"
```

---

### Task 3: Verification

- [ ] **Step 1: Build + test**

Run: `dotnet build && dotnet test`
Expected: all green (no new tests; existing `NoteRepositoryTests` still cover `UpdateAsync`).

- [ ] **Step 2: Manual smoke**

Open a campaign with a note → click ✎ → change title and content → Save → list shows new values and the timestamp advances. Click ✎ again → Cancel → note unchanged. Reload page → edit persisted.
