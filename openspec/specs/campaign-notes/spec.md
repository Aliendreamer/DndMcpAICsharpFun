# campaign-notes Specification

## Purpose
TBD - created by archiving change campaign-notes. Update Purpose after archive.
## Requirements
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

### Requirement: User can edit an existing campaign note
The system SHALL allow the signed-in user to edit an existing note's title and content from the campaign detail page and persist the change. The edited note SHALL reflect the new title, content, and an updated timestamp after saving.

#### Scenario: User edits and saves a note
- **WHEN** the user activates "Edit" on a note, changes its title and/or content, and saves
- **THEN** the note is persisted with the new values via `NoteRepository.UpdateAsync`, its `UpdatedAt` advances, and the updated note is shown in the list

#### Scenario: User cancels an edit
- **WHEN** the user activates "Edit" on a note and then cancels
- **THEN** the note retains its original title and content and no update is persisted

#### Scenario: Edit is scoped to the campaign
- **WHEN** a note is edited
- **THEN** the update only affects the note belonging to the current campaign

