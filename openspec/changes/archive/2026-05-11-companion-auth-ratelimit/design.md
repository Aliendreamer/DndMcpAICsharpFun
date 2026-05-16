## Context

The companion is a Blazor Server app. Authentication in Blazor Server is handled via ASP.NET Core cookie middleware — the browser holds a cookie, the server validates it on each request, and the Blazor circuit inherits the `ClaimsPrincipal` from the HTTP context. This is the standard, well-documented approach for Blazor Server auth.

User data (accounts, and later campaigns/notes) lives in a dedicated SQLite database (`companion.db`) separate from the main app's `ingestion.db`. This keeps the companion self-contained and avoids coupling to the MCP server's schema.

Rate limiting targets the `SendAsync` call in `DndChatService` rather than the HTTP layer, because individual chat messages travel over the established SignalR/WebSocket connection and are invisible to HTTP middleware after the initial handshake.

## Goals / Non-Goals

**Goals:**

- Username + hashed password accounts in companion SQLite
- Cookie auth protecting all companion pages except `/login` and `/register`
- Per-IP sliding-window rate limiter on chat message sends (configurable limit)
- User identity available in Blazor components via `AuthenticationStateProvider`
- Graceful rate limit error shown in chat (not a crash or redirect)

**Non-Goals:**

- OAuth / social login (future)
- Roles or permissions (future, when campaign management arrives)
- Email verification or password reset (out of scope for now)
- Rate limiting per authenticated user (IP is sufficient for personal deployment)
- ASP.NET Core Identity (too heavy; simple custom auth is enough)

## Decisions

**Custom auth over ASP.NET Core Identity** — Identity brings EF Core migrations, role tables, token providers, and ~20 tables for a use case that needs exactly one: users with hashed passwords. A single `Users` table with `Id`, `Username`, `PasswordHash` is all that's needed, and it's trivial to extend for campaigns/notes later.

**Cookie auth over JWT** — Blazor Server is server-rendered; JWTs require extra wiring (storage, refresh). Cookies are natively supported and require no JavaScript.

**PBKDF2 for password hashing** — Available in `System.Security.Cryptography` with no extra packages. `Rfc2898DeriveBytes` with SHA-256 and 100,000 iterations matches OWASP recommendations.

**In-memory rate limiter in `DndChatService`** — A `ConcurrentDictionary<string, RateLimitEntry>` keyed by IP, with a sliding window counter. No external dependency. Sufficient for a single-instance deployment. `IHttpContextAccessor` provides the IP inside the scoped service.

**Separate `companion.db`** — Decouples companion user data from the MCP server's ingestion database. Easier to back up, reset, or migrate independently.

## Risks / Trade-offs

- **In-memory rate limiter resets on restart** → Acceptable; this is a personal tool, not a high-security system.
- **IP-based limiting breaks behind a NAT/proxy** → All users behind the same NAT share one limit bucket. Acceptable for personal/home use; can be improved with `X-Forwarded-For` handling later.
- **No password reset** → Users who forget their password need manual DB intervention. Acceptable until email is wired up.

## Open Questions

- Should the `Users` table live in `companion.db` or be co-located with `ingestion.db`? Decision: separate `companion.db` to keep concerns isolated.
