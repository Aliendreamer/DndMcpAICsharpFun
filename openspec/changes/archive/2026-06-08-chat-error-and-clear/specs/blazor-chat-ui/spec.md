## ADDED Requirements

### Requirement: Chat surfaces send failures with a dismissible banner
When sending a message fails due to an MCP or network error, the system SHALL display a dismissible error banner above the chat thread without altering the existing thread or clearing the user's typed input. The banner SHALL be dismissable by the user and SHALL clear automatically at the start of the next successful send.

#### Scenario: Send fails and banner appears
- **WHEN** the user sends a message and the MCP client call throws
- **THEN** a dismissible error banner ("Couldn't send message") is shown above the thread, the prior thread is unchanged, and the loading indicator is cleared

#### Scenario: User dismisses the banner
- **WHEN** the user clicks the banner's dismiss control
- **THEN** the banner is removed and the thread is unchanged

#### Scenario: Next successful send clears the banner
- **WHEN** a previous send failed and the user successfully sends a subsequent message
- **THEN** the error banner is no longer shown

### Requirement: User can permanently clear a conversation
The system SHALL provide a "Clear conversation" control that, after explicit confirmation, permanently deletes the signed-in user's persisted chat turns for the active conversation scope (campaign/hero) and clears the on-screen thread. Cleared history SHALL NOT replay on subsequent page loads.

#### Scenario: User confirms clearing
- **WHEN** the user activates "Clear conversation" and confirms the dialog
- **THEN** the user's `ChatTurn` rows for the active scope are deleted, the on-screen thread is emptied, and reloading the page shows no replayed history

#### Scenario: User cancels clearing
- **WHEN** the user activates "Clear conversation" and cancels the dialog
- **THEN** no rows are deleted and the thread is unchanged

#### Scenario: Delete is scoped to the active conversation
- **WHEN** the user clears a conversation scoped to one campaign
- **THEN** chat turns belonging to the user's other conversation scopes are not deleted
