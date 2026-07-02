## Why

The full-repo audit (`docs/audits/2026-07-02-full-repo-audit.md`) found the single Critical and the
largest verified Important cluster in **auth and deployment hardening for the internet-exposed VPS**.
A `test`/`test` login is seeded in every environment (guaranteed backdoor), the MCP surface has a
cross-tenant IDOR, the public `/retrieval/*` endpoints are anonymous and unthrottled, the one
rate-limit bucket is global and collapses behind a proxy, and the auth cookie is emitted without
`Secure`/`SameSite`. None of these are exploitable only in theory — the threat model is a public
host. This change closes the whole security posture gap as one coherent hardening pass.

Closes audit findings: **SEC-01, SEC-02 (Critical), SEC-03, SEC-04, SEC-05, SEC-06, SEC-07, SEC-08,
SEC-09, SEC-10, COR-02, COR-05, COR-06, COR-24**.

## What Changes

- **Kill the seeded backdoor (SEC-02):** gate the `test`/`test` seed behind `IsDevelopment()`, or
  remove it entirely.
- **Close the MCP IDOR (SEC-08):** scope `resolve_character_feature` (and any character-scoped MCP
  tool) to the calling user's identity, or move it off the shared-key MCP surface.
- **Protect the public retrieval surface (SEC-10):** apply a per-client rate-limit partition to
  `/retrieval/*` and clamp user-controlled `topK`.
- **Fix rate limiting (SEC-04, SEC-05, SEC-06, SEC-07):** partition the limiter per client, honour
  forwarded headers behind the proxy, apply or remove the dead `registration` policy, and bound the
  `ChatRateLimiter` dictionary growth.
- **Harden the auth surface (SEC-01, SEC-03):** `Secure`/`SameSite` cookie policy + forwarded-headers
  HTTPS awareness, and a `NotAuthorized` redirect on `AuthorizeRouteView`.
- **Remove weak default secrets (SEC-09, COR-24):** drop the `:-devXXXdev`/`:-devMcpKey` compose
  fallbacks (fail fast when unset) and keep `/metrics` off the public surface.
- **Close the auth test gaps (COR-02, COR-05, COR-06):** fail-closed admin-key test, `PasswordHasher`
  error-path tests, and a `ChatRateLimiter` concurrency test.

## Capabilities

### New Capabilities

- `security-hardening`: the app's authentication, authorization, rate-limiting, secret-handling, and
  transport-security posture for internet-exposed deployment.

### Modified Capabilities

<!-- None. This is a new cross-cutting security capability spec derived from the audit. -->

## Impact

- Modified: `Extensions/AppExtensions.cs` (seed gating), `Extensions/AuthExtensions.cs` (cookie
  policy), `Extensions/RateLimitExtensions.cs` (partitioning, dead policy), `Program.cs`
  (`UseForwardedHeaders`, retrieval rate limit), `Features/Mcp/DndMcpTools.cs` (IDOR fix),
  `Features/Chat/DndChatService.cs` + `ChatRateLimiter.cs`, `CompanionUI/Components/Routes.razor`,
  `docker-compose.yml` (default keys).
- New tests: admin-key fail-closed, `PasswordHasher.Verify` error paths, `ChatRateLimiter`
  concurrency.
- No data-model or migration changes.

## Non-goals

- Introducing a full identity provider / OAuth. Cookie auth stays; only its flags and coverage change.
- Reworking the MCP transport; only per-user authorization of character-scoped tools.
