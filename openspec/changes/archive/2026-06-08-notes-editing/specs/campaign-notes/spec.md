## ADDED Requirements

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
