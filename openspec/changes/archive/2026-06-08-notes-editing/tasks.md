## 1. Edit UI

- [x] 1.1 Add per-note edit state to `CampaignDetail.razor` (`_editingNoteId`, `_editTitle`, `_editContent`, `_editNoteError`)
- [x] 1.2 Add an "Edit" control on each note card that swaps it into an editable title input + content textarea
- [x] 1.3 Implement `SaveNoteEditAsync` calling `NoteRepository.UpdateAsync`, then refresh the list; implement `CancelNoteEdit`

## 2. Verification

- [x] 2.1 `dotnet build` (warnings-as-errors) green
- [x] 2.2 Manual: edit a note title+content, save, reload → persisted; cancel → unchanged
