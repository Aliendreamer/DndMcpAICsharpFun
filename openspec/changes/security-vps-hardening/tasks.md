## 1. Auth backdoor + MCP IDOR (Critical/Important)

- [ ] 1.1 Gate the `test`/`test` seed behind `IsDevelopment()` or remove it (SEC-02, `Extensions/AppExtensions.cs:13`)
- [ ] 1.2 Scope `resolve_character_feature` to the calling user's identity; deny cross-tenant snapshot ids (SEC-08, `Features/Mcp/DndMcpTools.cs:158`)

## 2. Rate limiting + public surface

- [ ] 2.1 Replace the global bucket with a `PartitionedRateLimiter` keyed by user/client IP (SEC-04, `Extensions/RateLimitExtensions.cs:14`)
- [ ] 2.2 Add `UseForwardedHeaders` with a trusted-proxy allowlist before rate limiting/auth (SEC-07, `Program.cs`)
- [ ] 2.3 Apply a per-client rate-limit policy to `/retrieval/*` and clamp `topK` (SEC-10, `Program.cs:134`)
- [ ] 2.4 Apply or remove the unused `registration` policy (SEC-05, `Extensions/RateLimitExtensions.cs:22`)
- [ ] 2.5 Bound `ChatRateLimiter` dictionary growth (eviction / MemoryCache) (SEC-06, `Features/Chat/ChatRateLimiter.cs:8`)

## 3. Transport + secrets hardening

- [ ] 3.1 Set cookie `SecurePolicy = Always` + explicit `SameSite` (SEC-03, `Extensions/AuthExtensions.cs:9`)
- [ ] 3.2 Add a `NotAuthorized` redirect to `/login` on `AuthorizeRouteView` (SEC-01, `CompanionUI/Components/Routes.razor:6`)
- [ ] 3.3 Remove `:-devXXXdev`/`:-devMcpKey` compose fallbacks; fail fast when unset (COR-24, `docker-compose.yml:21`)
- [ ] 3.4 Keep `/metrics` off the anonymous public surface (bind loopback or authorize) (SEC-09, `Program.cs:120`)

## 4. Auth-path test coverage

- [ ] 4.1 Test admin middleware fails closed on empty/unset key (COR-02, `.../Admin/AdminApiKeyMiddlewareTests.cs`)
- [ ] 4.2 Test `PasswordHasher.Verify` malformed/empty-hash paths return false (COR-05, `.../Auth/PasswordHasherTests.cs`)
- [ ] 4.3 Test `ChatRateLimiter` concurrent-access limit correctness (COR-06, `.../Chat/ChatRateLimiterTests.cs`)

## 5. Verify + close

- [ ] 5.1 `dotnet build` + `dotnet test` green; update `DndMcpAICsharpFun.http` / insomnia if any endpoint auth shape changed
- [ ] 5.2 Confirm each finding (SEC-01..10, COR-02/05/06/24) is addressed; mark the audit report items resolved
