## 1. Nested-route highlighting

- [x] 1.1 Verify the Campaigns `NavLink` stays active on `/campaigns/{id}` (default prefix match); confirm whether hero detail routes are nested under `/campaigns`
- [x] 1.2 If a detail route falls outside the prefix, add an explicit active rule so the parent stays highlighted

## 2. Signed-in user display

- [x] 2.1 Read the signed-in user's display name from authentication state in `MainLayout.razor`
- [x] 2.2 Render the username in the sidebar (near the logout link)

## 3. Verification

- [x] 3.1 `dotnet build` (warnings-as-errors) green
- [x] 3.2 Manual: navigate top-level + a campaign/hero detail and confirm Campaigns stays highlighted; username shows
