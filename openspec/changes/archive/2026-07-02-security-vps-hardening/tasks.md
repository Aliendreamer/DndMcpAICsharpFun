## 1. Auth backdoor + MCP IDOR (Critical/Important)

- [x] 1.1 Make the `test`/`test` seed configurable via a flag (default on for local; disable-able in production); keep the account for local testing (SEC-02, `Extensions/AppExtensions.cs:13`)
- [x] 1.2 SPLIT OUT — SEC-08 moved to its own change `mcp-user-scoped-authorization` (needs a design decision: thread identity through the loopback, or move resolution off the shared-key MCP surface). Tracked in the roadmap.

## 2. Rate limiting + public surface

- [x] 2.1 Replace the global bucket with a `PartitionedRateLimiter` keyed by user/client IP (SEC-04, `Extensions/RateLimitExtensions.cs:14`)
- [x] 2.2 Add `UseForwardedHeaders` with a trusted-proxy allowlist before rate limiting/auth (SEC-07, `Program.cs`)
- [x] 2.3 Apply a per-client rate-limit policy to `/retrieval/*` and clamp `topK` (SEC-10, `Program.cs:134`)
- [x] 2.4 Apply or remove the unused `registration` policy (SEC-05, `Extensions/RateLimitExtensions.cs:22`)
- [x] 2.5 Bound `ChatRateLimiter` dictionary growth (eviction / MemoryCache) (SEC-06, `Features/Chat/ChatRateLimiter.cs:8`)

## 3. Transport + secrets hardening

- [x] 3.1 Set cookie `SecurePolicy = Always` + explicit `SameSite` (SEC-03, `Extensions/AuthExtensions.cs:9`)
- [x] 3.2 Add a `NotAuthorized` redirect to `/login` on `AuthorizeRouteView` (SEC-01, `CompanionUI/Components/Routes.razor:6`)
- [x] 3.3 Remove `:-devXXXdev`/`:-devMcpKey` compose fallbacks; fail fast when unset (COR-24, `docker-compose.yml:21`)
- [x] 3.4 Keep `/metrics` off the anonymous public surface (bind loopback or authorize) (SEC-09, `Program.cs:120`)

## 4. Auth-path test coverage

- [x] 4.1 Test admin middleware fails closed on empty/unset key (COR-02, `.../Admin/AdminApiKeyMiddlewareTests.cs`)
- [x] 4.2 Test `PasswordHasher.Verify` malformed/empty-hash paths return false (COR-05, `.../Auth/PasswordHasherTests.cs`)
- [x] 4.3 Test `ChatRateLimiter` concurrent-access limit correctness (COR-06, `.../Chat/ChatRateLimiterTests.cs`)

## 5. Verify + close

- [x] 5.1 `dotnet build` + `dotnet test` green; update `DndMcpAICsharpFun.http` / insomnia if any endpoint auth shape changed
- [x] 5.2 Confirm each finding (SEC-01..10, COR-02/05/06/24) is addressed; mark the audit report items resolved
