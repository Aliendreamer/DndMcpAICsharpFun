## 1. Verify as-built (Campaigns, Heroes)

- [x] 1.1 Confirm `Campaigns.razor` empty-state branch matches the spec scenario; adjust copy only if mismatched
- [x] 1.2 Confirm `Heroes.razor` empty-state branch matches the spec scenario

## 2. Chat empty state

- [x] 2.1 Add an empty-state block to `Chat.razor` shown when the message list is empty
- [x] 2.2 Ensure the block disappears once the first message is added

## 3. Verification

- [x] 3.1 `dotnet build` (warnings-as-errors) green
- [x] 3.2 Manual: new user sees all three empty states; they clear once content exists
