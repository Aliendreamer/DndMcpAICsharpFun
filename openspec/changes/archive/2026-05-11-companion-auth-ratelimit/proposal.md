## Why

The companion chat UI is currently open to anyone who can reach port 5102. As the companion grows to include campaign management, session notes, and character data, these features need to be tied to a specific user identity. Adding user accounts now establishes the identity foundation everything else can build on — and protects the Ollama hardware from unauthorized use in the meantime.

## What Changes

- Register and login pages added to the companion Blazor app
- User accounts stored in a SQLite database local to the companion (`companion.db`)
- Cookie-based authentication protecting the chat page and all future companion pages
- Per-IP rate limiting on chat messages (configurable, default 10 messages per minute) to protect Ollama from flooding

## Capabilities

### New Capabilities

- `companion-user-auth`: Registration, login, logout flows with cookie authentication; users stored in companion SQLite DB with hashed passwords
- `companion-chat-ratelimit`: Per-IP sliding-window rate limiter on `DndChatService.SendAsync`; returns a user-visible error message when the limit is exceeded

### Modified Capabilities

- `blazor-chat-ui`: Chat page requires authenticated user; unauthenticated requests redirect to `/login`

## Impact

- **New dependency**: `Microsoft.AspNetCore.Authentication.Cookies` (already in the ASP.NET Core shared framework — no NuGet package needed)
- **New SQLite DB**: `data/companion.db` mounted via Docker Compose volume
- **Config**: `RateLimit:MessagesPerMinute` (default 10) in companion `appsettings.json`
- **No changes to the MCP server or main app**
- **No breaking changes** to existing chat functionality once logged in
