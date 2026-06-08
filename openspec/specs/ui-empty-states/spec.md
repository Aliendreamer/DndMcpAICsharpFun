# ui-empty-states Specification

## Purpose
TBD - created by archiving change list-empty-states. Update Purpose after archive.
## Requirements
### Requirement: Campaigns list shows an empty state
When the signed-in user has no campaigns and the create form is not open, the Campaigns page SHALL show a friendly empty-state message prompting them to create their first campaign.

#### Scenario: No campaigns yet
- **WHEN** the user opens the Campaigns page with zero campaigns and no create form open
- **THEN** an empty-state message inviting them to create a campaign is shown instead of a list

### Requirement: Heroes list shows an empty state
When the signed-in user has no heroes, the Heroes page SHALL show a friendly empty-state message indicating heroes are created inside a campaign.

#### Scenario: No heroes yet
- **WHEN** the user opens the Heroes page with zero heroes
- **THEN** an empty-state message directing them to create heroes inside a campaign is shown instead of a list

### Requirement: Chat shows an empty state when there is no history
When the active conversation has no messages, the Chat page SHALL show a friendly empty-state prompt instead of a blank thread.

#### Scenario: Fresh conversation
- **WHEN** the user opens Chat with no prior messages in the active scope
- **THEN** an empty-state prompt inviting them to ask a question is shown

#### Scenario: Empty state clears once a message exists
- **WHEN** the user sends or receives the first message
- **THEN** the empty-state prompt is replaced by the message thread

