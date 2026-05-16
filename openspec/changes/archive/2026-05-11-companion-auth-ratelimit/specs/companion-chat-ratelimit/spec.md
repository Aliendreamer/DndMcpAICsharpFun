## ADDED Requirements

### Requirement: Chat messages are rate-limited per IP
The companion SHALL enforce a per-IP sliding-window rate limit on chat message sends. The limit SHALL be configurable via `RateLimit:MessagesPerMinute` (default 10). When the limit is exceeded, a user-visible error message SHALL appear in the chat instead of sending the message.

#### Scenario: Message sent within limit

- **WHEN** a user sends a message and has not exceeded the per-IP limit
- **THEN** the message is processed normally

#### Scenario: Rate limit exceeded

- **WHEN** a user sends more messages than `RateLimit:MessagesPerMinute` within a 60-second window
- **THEN** no request is sent to Ollama and an error message ("You are sending messages too fast. Please wait a moment.") appears in the chat

#### Scenario: Limit resets after window expires

- **WHEN** a user who hit the rate limit waits 60 seconds without sending
- **THEN** they can send messages again normally

### Requirement: Rate limit is enforced in DndChatService
The rate limiter SHALL be applied inside `DndChatService.SendAsync` using the caller's IP address obtained via `IHttpContextAccessor`. The check SHALL occur before calling Ollama.

#### Scenario: Rate limiter checks IP before Ollama call

- **WHEN** `SendAsync` is called and the IP is over the limit
- **THEN** Ollama is NOT called and the error message is returned immediately
