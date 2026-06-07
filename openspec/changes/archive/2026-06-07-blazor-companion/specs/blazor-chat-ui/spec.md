## ADDED Requirements
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
