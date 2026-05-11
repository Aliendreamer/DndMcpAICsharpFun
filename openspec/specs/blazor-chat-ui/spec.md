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

