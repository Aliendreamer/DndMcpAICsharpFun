# blazor-chat-ui Specification

## Purpose
TBD - created by archiving change companion-auth-ratelimit. Update Purpose after archive.
## Requirements
### Requirement: Chat page requires authenticated user
The chat page SHALL only be accessible to authenticated users. Unauthenticated requests to `/` SHALL be redirected to `/login`. The chat page SHALL display the logged-in username and a Logout button.

#### Scenario: Unauthenticated access redirected

- **WHEN** an unauthenticated user navigates to the companion root URL
- **THEN** they are redirected to `/login`

#### Scenario: Authenticated user sees chat

- **WHEN** a logged-in user navigates to the companion root URL
- **THEN** the chat page is displayed with their username and a Logout button visible

#### Scenario: Page loads with a greeting

- **WHEN** an authenticated user navigates to the companion root URL
- **THEN** the chat page is displayed with an initial assistant greeting message

#### Scenario: User sends a message

- **WHEN** the user types a message and clicks Send (or presses Enter)
- **THEN** the user message appears in the conversation list and the input field is cleared

#### Scenario: Loading indicator shown while waiting

- **WHEN** a message has been sent and the AI has not yet responded
- **THEN** a loading indicator is visible and the input is disabled

#### Scenario: Assistant reply appears after AI responds

- **WHEN** the AI returns a response
- **THEN** the assistant message is appended to the conversation list and the loading indicator is hidden

### Requirement: Chat page renders a conversation history and input
The companion SHALL display a scrollable list of messages (user and assistant alternating) and a text input with a Send button at the bottom of the page.

#### Scenario: Page loads with a greeting

- **WHEN** a user navigates to the companion root URL
- **THEN** the chat page is displayed with an initial assistant greeting message

#### Scenario: User sends a message

- **WHEN** the user types a message and clicks Send (or presses Enter)
- **THEN** the user message appears in the conversation list and the input field is cleared

#### Scenario: Loading indicator shown while waiting

- **WHEN** a message has been sent and the AI has not yet responded
- **THEN** a loading indicator is visible and the input is disabled

#### Scenario: Assistant reply appears after AI responds

- **WHEN** the AI returns a response
- **THEN** the assistant message is appended to the conversation list and the loading indicator is hidden

### Requirement: Conversation history persists within the browser session
The companion SHALL retain the full conversation history for the lifetime of the browser tab. Refreshing the page resets the history.

#### Scenario: History survives navigation within the tab

- **WHEN** the user sends multiple messages in the same tab
- **THEN** all messages remain visible in the conversation list

#### Scenario: Refresh clears history

- **WHEN** the user refreshes the browser page
- **THEN** the conversation history is empty and only the greeting is shown

### Requirement: AI errors are surfaced gracefully
The companion SHALL display a user-friendly error message when the AI is unavailable rather than crashing or showing a blank response.

#### Scenario: Ollama unreachable

- **WHEN** the Ollama service is not reachable and the user sends a message
- **THEN** an error message is shown in the conversation list and the user can try again

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

