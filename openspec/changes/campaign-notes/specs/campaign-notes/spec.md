## ADDED Requirements

### Requirement: Notes are scoped to a campaign
The system SHALL persist free-form notes (title + content) associated with a campaign and the user who created them, in a `Notes` table within `AppDbContext`.

#### Scenario: Create and list
- **WHEN** a user adds a note to a campaign
- **THEN** the note is stored with its title, content, campaign id, owner, and timestamps, and appears in that campaign's note list

#### Scenario: Listed newest first
- **WHEN** a campaign's notes are listed
- **THEN** they are ordered by most-recently-updated first

#### Scenario: Scoped to the campaign
- **WHEN** notes are listed for a campaign
- **THEN** only that campaign's notes are returned, not notes from other campaigns

### Requirement: Notes can be deleted
The system SHALL allow deleting a note from its campaign.

#### Scenario: Delete removes the note
- **WHEN** a user deletes a note
- **THEN** it no longer appears in the campaign's note list

### Requirement: Deleting a campaign deletes its notes
The system SHALL delete a campaign's notes when the campaign is deleted, leaving no orphaned notes.

#### Scenario: Cascade on campaign delete
- **WHEN** a campaign with notes is deleted
- **THEN** those notes are removed
