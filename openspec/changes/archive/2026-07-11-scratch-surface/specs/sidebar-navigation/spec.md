## MODIFIED Requirements

### Requirement: Sidebar highlights the active route
The sidebar SHALL render navigation links for Chat, Campaigns, Heroes, and Scratchpad, and SHALL visually highlight the link corresponding to the current route.

#### Scenario: Top-level route is highlighted
- **WHEN** the user is on `/campaigns`
- **THEN** the Campaigns link is shown as active and the other links are not

#### Scenario: Chat link uses exact match
- **WHEN** the user is on `/` (Chat)
- **THEN** the Chat link is active and is not shown as active on other routes

#### Scenario: Scratchpad link is highlighted on its route
- **WHEN** the user is on `/scratch`
- **THEN** the Scratchpad link is shown as active and the other links are not
