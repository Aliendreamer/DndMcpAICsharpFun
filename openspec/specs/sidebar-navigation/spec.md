# sidebar-navigation Specification

## Purpose
TBD - created by archiving change nav-active-highlight. Update Purpose after archive.
## Requirements
### Requirement: Sidebar highlights the active route
The sidebar SHALL render navigation links for Chat, Campaigns, and Heroes, and SHALL visually highlight the link corresponding to the current route.

#### Scenario: Top-level route is highlighted
- **WHEN** the user is on `/campaigns`
- **THEN** the Campaigns link is shown as active and the other links are not

#### Scenario: Chat link uses exact match
- **WHEN** the user is on `/` (Chat)
- **THEN** the Chat link is active and is not shown as active on other routes

### Requirement: Parent nav stays active on nested detail routes
When the user navigates to a nested detail route under a top-level section, the sidebar SHALL keep that section's link highlighted.

#### Scenario: Campaign detail keeps Campaigns active
- **WHEN** the user is on `/campaigns/{id}` or a hero detail route nested under a campaign
- **THEN** the Campaigns link remains highlighted as active

### Requirement: Sidebar shows the signed-in user
The sidebar SHALL display the signed-in user's name.

#### Scenario: Signed-in user name is shown
- **WHEN** an authenticated user views any page with the sidebar
- **THEN** their display name is shown in the sidebar

