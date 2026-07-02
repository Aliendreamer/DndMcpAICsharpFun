# Full Repository Audit — 2026-07-02

Audited commit: `901892f0903fc2c94e5a14d224041bce2074f5c7` ·
Threat model: internet-exposed VPS (`[vps]` = exposure-only risk)

## Summary

An 83-finding, five-dimension audit of the whole repository (security,
structure, .NET best-practices, simplification, correctness) run as a
read-only agent fan-out over a 368-file manifest with 100% ledger coverage.
Every Critical and Important finding was put through an adversarial refute
pass: of 21 candidates, 15 were **confirmed**, 6 were **downgraded** to
Minor, and **none survived as false positives**.

| Severity | Count |
| --- | --- |
| Critical | 1 |
| Important | 14 |
| Minor | 68 |
| **Total** | **83** |

By dimension (post-verification):

| Dimension | Critical | Important | Minor |
| --- | --- | --- | --- |
| Security | 1 | 5 | 4 |
| Correctness | 0 | 6 | 19 |
| Structure | 0 | 3 | 14 |
| .NET best-practices | 0 | 0 | 12 |
| Simplification | 0 | 0 | 19 |

**Headline findings:**

1. **SEC-02 (Critical)** — a `test`/`test` login is seeded unconditionally in
   every environment, a guaranteed credential backdoor on a public host.
2. **SEC-08 (Important)** — the MCP `resolve_character_feature` tool has an
   IDOR: any snapshot id resolves without an ownership check, exposing other
   users' hero data.
3. **COR-16 / COR-17 (Important)** — BM25 sparse-vector indices come from
   `String.GetHashCode()`, which is per-process randomized in .NET; hybrid
   keyword retrieval silently breaks after every restart.
4. **SEC-10 / SEC-04 / SEC-07 (Important)** — the public `/retrieval/*`
   endpoints are anonymous *and* unthrottled, and the one rate-limit bucket
   that exists is global (not per-client) and collapses behind a proxy
   because `UseForwardedHeaders` is never configured.
5. **COR-24 (Important)** — the dev compose bakes weak, repo-committed default
   Admin/MCP keys (`devXXXdev`, `devMcpKey`) and defaults the environment to
   Development.

**Overall posture:** the codebase is clean structurally where it counts — no
vulnerable NuGet packages, a warnings-as-errors build with zero warnings, no
hardcoded secret literals in source, and consistent repository/DbContext
patterns. The real risk concentration is **deployment and auth hardening for
the internet-exposed VPS**: a seeded backdoor credential, weak default keys in
compose, missing forwarded-headers/HTTPS cookie hardening, and unauthenticated
public endpoints. The one systemic *correctness* defect is the
non-deterministic BM25 term hashing, which quietly degrades retrieval quality.
The bulk of Minor findings are duplication and dead-code cleanups plus test
hygiene, none of which block correctness today.

## Tooling evidence

- **NuGet vulnerabilities:** `dotnet list package --vulnerable
  --include-transitive` across all three projects — **no vulnerable packages**.
- **Build baseline:** main project compiles clean, **0 warnings / 0 errors**
  under warnings-as-errors (`Directory.Build.props`). (`dotnet build` and
  `dotnet format --verify-no-changes` could not fully run inside the audit
  sandbox — blocked only on copying the sandbox-masked `Config/appsettings*`
  secrets and slnx restore, not on any code issue; analyzer enforcement is
  covered by the warnings-as-errors build.)
- **Secret-pattern scan:** grep for hardcoded
  `password|secret|apikey|token` literals across `.cs/.yml/.props/.razor` —
  **0 hits** (the weak keys in COR-24 live in compose `:-default` fallbacks and
  the insomnia collection, not in source).
- **Endpoints:** 22 `Map*` registrations plus the `/mcp` MCP surface and the
  Blazor UI (see posture table below).

## Endpoint auth posture

Auth is fully custom — there is **no** standard ASP.NET
`AddAuthentication`/`RequireAuthorization`/`AllowAnonymous` anywhere. Admin and
MCP routes are guarded by two constant-time-comparing middlewares
(`AdminApiKeyMiddleware`, `McpAuthMiddleware`), each path-scoped via
`app.UseWhen(ctx => ctx.Request.Path.StartsWithSegments(...))` and each
**fail-closed** (empty/unset key returns 401). Blazor pages use cookie auth.

| Endpoint(s) | Method | Classification | Notes |
| --- | --- | --- | --- |
| `/admin/*` (19 routes: books register/list/delete, ingest-blocks, ingest-entities, extract-entities, backfill-spells, project-structured, canonical normalize/validate/fix-types, 5etools import/sources, entities needs-review/resolve/accept, retrieval search, entities search) | GET/POST/DELETE | Admin-key (`X-Admin-Api-Key`) | `UseWhen("/admin")` then `AdminApiKeyMiddleware`; several POSTs `.DisableAntiforgery()`. Weak default key via compose (COR-24). |
| `/mcp` (MCP tool surface) | — | MCP-key | `UseWhen("/mcp")` then `McpAuthMiddleware`; single shared `Mcp:ApiKey` with no per-user identity, giving IDOR on character tools (SEC-08). |
| `/retrieval/search` | GET | **Anonymous** | No auth, no rate limit; drives Ollama embedding + Qdrant search with user-controlled `topK` (SEC-10). |
| `/retrieval/entities/{id}` | GET | **Anonymous** | No auth, no rate limit (SEC-10). |
| `/retrieval/entities/search` | GET | **Anonymous** | No auth, no rate limit (SEC-10). |
| `/metrics` | GET | **Anonymous** | Only mapped when `OpenTelemetry:Enabled` (default false); recon info-leak when on (SEC-09, Minor). |
| `/health`, `/ready` | GET | Anonymous | Liveness/readiness (compose healthcheck probes bare TCP instead — COR-25, Minor). |
| Blazor UI (`/login`, `/logout`, components) | GET/POST | User-cookie | `RequireRateLimiting("global")` — one shared un-partitioned bucket (SEC-04); cookie lacks `Secure`/`SameSite` (SEC-03). |

## Critical findings

### SEC-02 — Unconditional seeding of a known 'test'/'test' login in every environment

- **Where:** `Extensions/AppExtensions.cs:13` · **Dimension:** security · **Effort:** S · **Verified:** CONFIRMED · `[vps]`
- **What:** InitializeDatabaseAsync() (invoked unconditionally from Program.cs at app startup) creates a user 'test' with password 'test' whenever that username is absent. It is not gated to the Development environment.
- **Why it matters:** On an internet-exposed VPS this is a guaranteed backdoor: anyone can log in at /login with test/test and gain full authenticated Blazor access (campaigns, heroes, chat/LLM). Weak, publicly-known credentials = trivial auth bypass.
- **Recommendation:** Gate the seed behind app.Environment.IsDevelopment(), or remove it and provision the first account via a one-off admin-key-protected path. Never seed a fixed weak credential in Production.

## Important findings

### COR-12 — Container runs as root; no non-privileged USER

- **Where:** `Dockerfile:10` · **Dimension:** correctness · **Effort:** S · **Verified:** CONFIRMED · `[vps]`
- **What:** The aspnet runtime stage never adds a non-root user or a USER directive, so the process runs as root inside the container. | ALSO: No HEALTHCHECK defined for the container — The image exposes port 5101 and the app has /health and /ready endpoints, but the Dockerfile declares no HEALTHCHECK.
- **Why it matters:** On an internet-exposed VPS a compromise of the app (e.g. via the ingestion/LLM surface) grants root inside the container, widening blast radius and easing container escape.
- **Recommendation:** Create and switch to a non-root user (e.g. `USER $APP_UID` on the aspnet image, or `useradd` a service user) and chown /books and /data to it.

### COR-15 — BM25 IDF/avgDocLen computed per 32-doc batch instead of over the corpus

- **Where:** `Features/Ingestion/Bm25Vectorizer.cs:40` · **Dimension:** correctness · **Effort:** L · **Verified:** CONFIRMED
- **What:** ComputeBatch derives document frequency (`docFrequencies`), corpus size `n`, and `avgDocLen` from only the docs in the current call (batches of 32 from BlockIngestionOrchestrator, or a single doc at query time).
- **Why it matters:** IDF is meant to be corpus-global. Identical text in two different ingest batches gets different sparse weights, and a single-document query batch always yields df=1,n=1 -> idf=log(2/2)+1=1 for every term, erasing all IDF weighting. Hybrid ranking is inconsistent between ingest and query and between batches.
- **Recommendation:** Compute IDF and average document length from a persisted corpus-wide statistics table (or a fixed precomputed IDF map), not per-batch; at query time reuse those global statistics.

### COR-16 — BM25 sparse index derived from non-deterministic String.GetHashCode()

- **Where:** `Features/Ingestion/Bm25Vectorizer.cs:62` · **Dimension:** correctness · **Effort:** M · **Verified:** CONFIRMED
- **What:** Term-to-index mapping uses `Math.Abs(term.GetHashCode()) % VocabSize`. In .NET Core+, String.GetHashCode() is randomized per process start, so the same term hashes to different indices in different process lifetimes. | ALSO: Math.Abs(term.GetHashCode()) can throw OverflowException — GetHashCode() may return int.MinValue, for which Math.Abs throws OverflowException, aborting the whole batch upsert.
- **Why it matters:** Sparse vectors written to Qdrant during ingestion are matched against sparse query vectors computed in a later run. After any app restart the query-time hashing differs from the ingest-time hashing, so sparse (BM25) hybrid retrieval silently stops matching the stored vectors, degrading or breaking keyword retrieval with no error.
- **Recommendation:** Replace GetHashCode with a stable hash (e.g. FNV-1a / xxHash over the UTF-8 bytes, or a fixed vocabulary map) so the term->index mapping is deterministic across processes.

### COR-17 — BM25 sparse-vector indices derived from String.GetHashCode are not stable across processes

- **Where:** `Features/Ingestion/Bm25Vectorizer.cs:69` · **Dimension:** correctness · **Effort:** S · **Verified:** CONFIRMED
- **What:** Bm25Vectorizer computes each token's sparse index as Math.Abs(term.GetHashCode()) % VocabSize. In .NET Core/.NET 10, String.GetHashCode() is randomized per process (seeded at startup) and is NOT guaranteed stable across runs. The ingestion path (BlockIngestionOrchestrator.IngestBlocksAsync) writes these sparse vectors to Qdrant in one process; queries run in a later process with a different hash seed.
- **Why it matters:** After any app restart, the BM25 index token->dimension mapping changes, so query-time sparse vectors no longer align with the indices persisted at ingestion time. The sparse/hybrid retrieval component (QueryAsync path when SparseSupported=true) silently degrades to near-zero recall for keyword matches, with no error surfaced. The unit test Bm25VectorizerTests.cs:45-46 reproduces the exact GetHashCode expression in-process, so it always passes and can never catch this cross-process divergence.
- **Recommendation:** Replace GetHashCode with a deterministic hash (e.g. a fixed FNV-1a/xxHash over the UTF-8 bytes, or a stable managed hash) for the sparse index, and add a test asserting a known token maps to a fixed expected index (golden value) rather than recomputing it via GetHashCode.

### COR-23 — Production compose never provides the 5etools data directory, silently disabling import and spell-backfill

- **Where:** `docker-compose.prod.yml:20` · **Dimension:** correctness · **Effort:** S · **Verified:** CONFIRMED
- **What:** The Dockerfile publishes from /app/publish and never copies the /src/5etools tree into the runtime image (grep of Dockerfile shows no 5etools COPY; only `COPY --from=build /app/publish .`). The dev docker-compose.yml bind-mounts `./5etools:/app/5etools:ro`, but docker-compose.prod.yml has no equivalent mount. FivetoolsSourceRegistry.Build, FivetoolsRecordIndex.BuildAsync and SpellBackfillService.EnumerateFivetoolsSpells all guard with Directory.Exists and return empty/no-op when the directory is absent.
- **Why it matters:** In production the /admin/5etools/import, /admin/canonical/fix-types and /admin/books/{id}/backfill-spells endpoints, plus entity enrichment via FivetoolsRecordIndex, all silently do nothing and report success (0 imported / empty result). This is a functional feature loss with no error surfaced, hard to diagnose.
- **Recommendation:** Either bake the 5etools data into the image (COPY it into the publish output) or add a `./5etools:/app/5etools:ro` volume to docker-compose.prod.yml, and log a warning when the directory is missing instead of silently returning empty.

### COR-24 — Dev compose bakes a publicly-known default admin key and defaults environment to Development

- **Where:** `docker-compose.yml:21` · **Dimension:** correctness · **Effort:** S · **Verified:** CONFIRMED · `[vps]`
- **What:** `Admin__ApiKey=${ADMIN_API_KEY:-devXXXdev}` and `ASPNETCORE_ENVIRONMENT=${ASPNETCORE_ENVIRONMENT:-Development}` hardcode a weak fallback admin key (the same `devXXXdev` value published in dnd-mcp-api.insomnia.json) and a Development default. | ALSO (security): Weak known-default fallback values for Admin and MCP API keys — docker-compose.yml defaults Admin__ApiKey to 'devXXXdev' (line 21) and Mcp__ApiKey/McpClient__ApiKey to 'devMcpKey' (lines 26-28) when the env vars are unset. These same literals appear in DndMcpAICsharpFun.http and dnd-mcp-api.insomnia.json.
- **Why it matters:** If this compose file is ever brought up on the internet-exposed VPS without setting ADMIN_API_KEY, every admin endpoint is protected by a key that is checked into the repo and shipped in the API collection, i.e. effectively unauthenticated. | If this compose file is used to deploy on a VPS without exporting ADMIN_API_KEY/MCP_API_KEY, the admin surface (book ingest, delete, canonical mutation) and the MCP tool surface are protected only by publicly-known keys committed to the repo.
- **Recommendation:** Remove the inline default for Admin__ApiKey (fail fast when unset) and do not default ASPNETCORE_ENVIRONMENT to Development for any host-facing compose. | Remove the :-default fallbacks so startup fails fast when the keys are unset (as docker-compose.prod.yml already does), and document that both keys are mandatory.

### SEC-03 — Auth cookie does not force Secure / SameSite; no HTTPS hardening

- **Where:** `Extensions/AuthExtensions.cs:9` · **Dimension:** security · **Effort:** S · **Verified:** CONFIRMED · `[vps]`
- **What:** AddCookie only sets LoginPath/LogoutPath. Cookie.SecurePolicy is left at the default SameAsRequest and SameSite is unset (Lax). The app runs plain HTTP behind a TLS-terminating proxy (no UseHttpsRedirection/UseHsts), so the internal hop is HTTP and the auth cookie is emitted without the Secure attribute.
- **Why it matters:** A session cookie without Secure can be transmitted/observed over a non-TLS hop and is more exposed to network interception, weakening session protection on a VPS.
- **Recommendation:** Set options.Cookie.SecurePolicy = CookieSecurePolicy.Always, an explicit SameSite, and configure UseForwardedHeaders so the framework knows requests are HTTPS.

### SEC-04 — 'global' rate-limit policy is a single shared bucket, not partitioned per client

- **Where:** `Extensions/RateLimitExtensions.cs:14` · **Dimension:** security · **Effort:** M · **Verified:** CONFIRMED
- **What:** AddSlidingWindowLimiter("global") creates one limiter instance shared by all requests, applied via RequireRateLimiting("global") to the entire Blazor component endpoint and /logout. Default PermitLimit is 60/minute for the whole app combined.
- **Why it matters:** A single un-partitioned bucket means any user's traffic (or an attacker's) exhausts the limit for all users (DoS), while providing no meaningful per-client abuse protection. 60/min total is also far too low for interactive Blazor Server circuits.
- **Recommendation:** Use PartitionedRateLimiter keyed by authenticated user or client IP (via forwarded headers), and size the limit per client rather than globally.

### SEC-07 — Per-IP rate limiting collapses behind reverse proxy (no ForwardedHeaders)

- **Where:** `Features/Chat/DndChatService.cs:37` · **Dimension:** security · **Effort:** S · **Verified:** CONFIRMED · `[vps]`
- **What:** ChatRateLimiter and the registration throttle key on HttpContext.Connection.RemoteIpAddress (DndChatService.cs:37, Register.razor:55), but the app never configures UseForwardedHeaders. Behind the expected reverse proxy every request presents the proxy's IP.
- **Why it matters:** All clients share a single rate-limit bucket: one abuser can lock out everyone, and per-IP registration/chat throttles provide no real per-client protection. Real client IPs are also lost from logs.
- **Recommendation:** Add app.UseForwardedHeaders with a trusted-proxy allowlist before rate limiting/auth so RemoteIpAddress reflects the real client.

### SEC-08 — MCP resolve_character_feature exposes any user's hero snapshot (IDOR)

- **Where:** `Features/Mcp/DndMcpTools.cs:158` · **Dimension:** security · **Effort:** M · **Verified:** CONFIRMED
- **What:** resolve_character_feature takes an arbitrary heroSnapshotId and calls resolutionService.ResolveAsync with no ownership/authorization check. All MCP tools authenticate only with the shared Mcp:ApiKey and carry no per-user identity; the loopback chat client uses this key on behalf of every signed-in user.
- **Why it matters:** A user (via a crafted chat prompt) or any holder of the MCP key can read rule facts derived from other users' hero snapshots by iterating snapshot ids, a cross-tenant data-access flaw.
- **Recommendation:** Scope hero/snapshot access by the calling user's identity, or keep character-scoped tools out of the shared-key MCP surface and resolve them only inside the authenticated Blazor session.

### SEC-10 — Public /retrieval/* endpoints are anonymous and unthrottled

- **Where:** `Program.cs:134` · **Dimension:** security · **Effort:** M · **Verified:** CONFIRMED · `[vps]`
- **What:** MapRetrievalEndpoints/MapEntityRetrievalEndpoints register /retrieval/search, /retrieval/entities/{id}, /retrieval/entities/search with no auth middleware (only /admin and /mcp are guarded) and no RequireRateLimiting.
- **Why it matters:** Unauthenticated internet users can drive unbounded embedding generation (Ollama) and Qdrant vector searches, a cheap resource-exhaustion / cost-abuse and scraping vector on a VPS.
- **Recommendation:** Apply the rate-limiter policy to the public retrieval group and/or require authentication; at minimum add RequireRateLimiting with a per-client partition.

### STR-09 — God file: EntityExtractionOrchestrator mixes 6+ distinct responsibilities in 657 lines

- **Where:** `Features/Ingestion/EntityExtraction/EntityExtractionOrchestrator.cs:12` · **Dimension:** structure · **Effort:** L · **Verified:** CONFIRMED
- **What:** EntityExtractionOrchestrator takes 16 constructor dependencies (tracker, registry, PDF converter/bookmark reader, candidate scanner, stat-block scanner, canonical writer, 3 side-file writers, reference resolver, schema provider, checkpoint store, candidate extractor, options, logger, optional matcher) and its class body spans lines 12-656. Within it: ExtractAsync (34-144) drives PDF conversion + TOC derivation + candidate scanning/dedup; RunFullExtractionAsync (146-285) and RunErrorsOnlyAsync (287-420) each independently implement ~130-line checkpoint/retry loops with near-duplicate reference-resolution/warning-partition/file-write logic; ExtractOneAsync (453-536) implements the forced-type vs union-extraction branching; BuildTypedEnvelope/DeclinedEnvelope/HasGroundedContent/BuildScannerInputs round out the file with unrelated envelope-construction and PDF-item-projection concerns.
- **Why it matters:** A single class this wide is hard to unit test in isolation (every test must stand up the full 16-dependency graph), hard to reason about (candidate scanning, LLM dispatch, checkpointing, and file I/O are interleaved), and the duplicated ~40-line reference-resolution/warning block between RunFullExtractionAsync and RunErrorsOnlyAsync is a drift risk — a fix applied to one path (e.g. how intra-book dangling refs are classified) can silently miss the other.
- **Recommendation:** Split into: (1) a CandidatePipeline component (BuildScannerInputs + scan + dedup + drop-filter, i.e. the first half of ExtractAsync), (2) an ExtractionRunner that owns the shared per-candidate loop and checkpoint/retry behavior with a mode flag or strategy for full-vs-errors-only, and (3) a small ExtractionResultWriter that owns the reference-resolution/warning-partition/file-write tail shared by both run modes. Keep EntityExtractionOrchestrator as a thin composition of these three.

### STR-14 — Features/VectorStore and Features/Retrieval both cross-reference Features/Ingestion, breaking feature-slice isolation

- **Where:** `Features/VectorStore/IVectorStoreService.cs:1` · **Dimension:** structure · **Effort:** M · **Verified:** CONFIRMED
- **What:** IVectorStoreService.cs (line 1) and QdrantVectorStoreService.cs (line 9) in Features/VectorStore import `DndMcpAICsharpFun.Features.Ingestion` to use `SparseVector`. Separately, Features/Retrieval/RagRetrievalService.cs (line 2) and Features/Retrieval/FusedRetrievalService.cs (line 2) import the same namespace to call `Bm25Vectorizer.ComputeBatch(...)` at query time. Three different feature slices (VectorStore, Retrieval, and transitively Infrastructure/Qdrant) now directly depend on internal types/logic that live inside the Ingestion slice.
- **Why it matters:** This is exactly the kind of cross-slice coupling the vertical-slice structure is meant to prevent: a change to Ingestion's SparseVector shape or Bm25Vectorizer term-hashing logic can silently break query-time retrieval and the vector-store upsert path in two unrelated slices, and none of VectorStore/Retrieval/Ingestion can be built, tested, or evolved independently. It also means the BM25 term-hashing used at ingest time and at query time is only kept consistent by convention (both sides `using` the same Ingestion-owned static class), not by an explicit shared contract.
- **Recommendation:** Extract SparseVector and Bm25Vectorizer into a shared, feature-neutral module (e.g. a `Search`/`Sparse` folder under Infrastructure or a small shared kernel referenced by all three slices), and depend on that from Ingestion, VectorStore, Retrieval, and Infrastructure/Qdrant alike.

### STR-15 — Infrastructure/Qdrant depends upward on a Features/Ingestion type, inverting the intended dependency direction

- **Where:** `Infrastructure/Qdrant/IQdrantSearchClient.cs:2` · **Dimension:** structure · **Effort:** M · **Verified:** CONFIRMED
- **What:** IQdrantSearchClient (Infrastructure layer) and its implementation QdrantSearchClientAdapter.cs (line 4) alias `DomainSparseVector = DndMcpAICsharpFun.Features.Ingestion.SparseVector` and use it directly in the interface signature (QueryAsync). Infrastructure/Qdrant is meant to be a low-level, feature-agnostic client wrapper, but it now has a compile-time dependency on a type owned by the Features/Ingestion vertical slice.
- **Why it matters:** Infrastructure should be the lowest layer that Features depend on, not the reverse. Any refactor of Features/Ingestion (e.g. renaming/moving SparseVector, or Ingestion picking up new dependencies) can now break the Infrastructure/Qdrant client, and Infrastructure/Qdrant cannot be reused, tested, or reasoned about independently of the Ingestion feature. It also makes the dependency graph harder to keep acyclic as more features consume Qdrant.
- **Recommendation:** Move SparseVector (and the closely related Bm25Vectorizer) out of Features/Ingestion into a neutral location such as Infrastructure/Qdrant or Domain, and have Features/Ingestion, Features/VectorStore and Features/Retrieval all depend on that shared type instead of on each other's feature namespaces.

## Minor findings

68 Minor findings, grouped by dimension. Six were **downgraded from
Important** during verification (real, but overstated impact) and are marked
inline. All were reported with a cited line; a random 10-finding spot-check
during synthesis found no misreads.

### Security (4)

| ID | Location | Finding | Recommendation | Effort |
| --- | --- | --- | --- | --- |
| SEC-01 | `CompanionUI/Components/Routes.razor:6` | AuthorizeRouteView has no NotAuthorized/redirect for unauthenticated users | Add a <NotAuthorized> block that redirects to /login (or shows a sign-in prompt) inside AuthorizeRouteView. | S |
| SEC-05 | `Extensions/RateLimitExtensions.cs:22` | 'registration' rate-limit policy defined but never applied | Either apply the 'registration' policy to the registration path or remove it and consolidate on one rate-limiting mechanism. | S |
| SEC-06 | `Features/Chat/ChatRateLimiter.cs:8` | ChatRateLimiter dictionary grows unbounded (no eviction) | Evict expired window counters (e.g. periodic sweep or use MemoryCache with sliding expiration), or switch to the framework PartitionedRateLimiter which manages lifetime. | S |
| SEC-09 | `Program.cs:120` | /metrics Prometheus endpoint is unauthenticated and publicly reachable *(downgraded from Important)* | Bind/scrape metrics on a loopback-only port, or place /metrics behind the admin key / an authorization policy, or restrict via reverse-proxy so it is never internet-exposed. | M |

### Structure (14)

| ID | Location | Finding | Recommendation | Effort |
| --- | --- | --- | --- | --- |
| STR-01 | `Domain/Hero.cs:1` | Domain entity carries an EF Core mapping attribute ([NotMapped]) *(downgraded from Important)* | Remove [NotMapped] from Hero and instead exclude LatestSnapshot via Fluent API (`entity.Ignore(h => h.LatestSnapshot)`) in the AppDbContext configuration, matching the rest of the Domain layer. | S |
| STR-02 | `Domain/IngestionRecord.cs:1` | Domain entity uses System.ComponentModel.DataAnnotations validation/mapping attributes, inconsistent with sibling Domain classes | Move MaxLength/Required constraints to Fluent API configuration in AppDbContext (or a dedicated IEntityTypeConfiguration<IngestionRecord>) to match the rest of the Domain layer's convention. | S |
| STR-03 | `Features/Admin/BooksAdminEndpoints.cs:26` | Admin slice has wide fan-out across five other Features/* slices plus Infrastructure | Document Admin as an intentional cross-cutting orchestration slice (not a peer vertical slice) in project docs, or introduce narrow façade interfaces per dependency to reduce the surface Admin binds to directly. | M |
| STR-04 | `Features/Admin/BooksAdminEndpoints.cs:195` | RegisterBook endpoint embeds ~80 lines of multipart-parsing business logic directly in the endpoint handler, unlike sibling Admin endpoint files | Extract the multipart parsing/validation into a dedicated BookRegistrationService (mirroring NeedsReviewService/CanonicalValidationService), leaving the endpoint handler as a thin adapter. | M |
| STR-05 | `Features/Admin/CanonicalTypeFixerService.cs:9` | Admin canonical-file services depend on two different Options classes for the same CanonicalDirectory concept | Consolidate on a single shared options type (or a common base) for the canonical-directory path used across Admin services, or clearly document why two separate options types exist. | M |
| STR-06 | `Features/Admin/CanonicalValidationEndpoints.cs:5` | Inconsistent endpoint-mapping convention within the Admin slice (WebApplication+hardcoded path vs RouteGroupBuilder group) | Standardize all Admin endpoint files on the RouteGroupBuilder-of-'/admin' pattern used by BooksAdminEndpoints/FivetoolsAdminEndpoints/NeedsReviewEndpoints, so the '/admin' prefix and its auth middleware are applied structurally rather than by convention/path-matching. | S |
| STR-07 | `Features/Ingestion/Entities/EntityIngestionOrchestrator.cs:1` | Features/Ingestion consistently reaches into Features/Entities as a de-facto shared kernel | No urgent action needed since the dependency is unidirectional and the coupling is inherent (ingestion renders/validates canonical entity JSON that Entities owns the model for). Consider documenting this in a README/ADR as an intentional shared-kernel relationship, or moving CanonicalJsonLoader/EntityCanonicalTextDispatcher/EntityReferenceResolver under Features/Ingestion/Entities if they are only ever consumed by ingestion code paths. | M |
| STR-08 | `Features/Ingestion/Entities/EntityIngestionOrchestrator.cs:68` | 5etools enrichment-index build + merge dispatch is duplicated between IngestEntitiesAsync and ReindexEntityAsync | Extract a private helper, e.g. `Task<EntityEnvelope> EnrichAsync(EntityEnvelope envelope, IReadOnlyDictionary<string,EntityEnvelope> fivetoolsIndex, IngestionRecord record, CancellationToken ct)`, and call it from both IngestEntitiesAsync and ReindexEntityAsync. | S |
| STR-10 | `Features/Ingestion/FivetoolsIngestion/FivetoolsRecordIndex.cs:17` | EntityType->mapper registry duplicated in two classes | Extract the registry into a single shared static (e.g. FivetoolsMapperRegistry.All) and reference it from both FivetoolsIngestionService and FivetoolsRecordIndex. | S |
| STR-11 | `Features/Ingestion/FivetoolsIngestion/SpellBackfillService.cs:28` | Edition2024Sources set duplicated across slices | Move the source set to a single shared helper (e.g. an Edition classifier in Domain.Entities or FivetoolsMapperBase) and reference it from both the mapper base and the backfill service. | S |
| STR-12 | `Features/Ingestion/Tracking/IIngestionTracker.cs:1` | Feature-slice interface imports the Infrastructure.Ingestion namespace unnecessarily | Remove the unused 'using DndMcpAICsharpFun.Infrastructure.Ingestion;' line. | S |
| STR-13 | `Features/Ingestion/Tracking/IngestionTracker.cs:8` | IngestionTracker takes AppDbContext directly, diverging from the IDbContextFactory pattern | Either adopt IDbContextFactory<AppDbContext> here for consistency with the other data-access types, or document why direct scoped injection is intentional for the ingestion worker scope. | M |
| STR-16 | `Program.cs:30` | Program.cs binds ten feature option types inline instead of delegating to each feature's DI extension method | Move each `AddOptions<T>().BindConfiguration(...).ValidateDataAnnotations().ValidateOnStart()` block into the corresponding feature's own `AddXyz(IServiceCollection, IConfiguration)` extension method so Program.cs only orchestrates top-level `Add*` calls. | M |
| STR-17 | `Program.cs:62` | Reranker DI wiring is done ad hoc in Program.cs with fully-qualified type names instead of via a feature extension method | Move the CrossEncoderReranker/IReranker registration into the existing `AddRetrieval()` extension method alongside RerankerOptions binding, and add the missing `using` directives if kept inline. | S |

### .NET best-practices (12)

| ID | Location | Finding | Recommendation | Effort |
| --- | --- | --- | --- | --- |
| NET-01 | `Features/Campaigns/CampaignRepository.cs:41` | Multi-step campaign deletion is not wrapped in a transaction *(downgraded from Important)* | Wrap the four ExecuteDeleteAsync calls in an explicit transaction (await using var tx = await db.Database.BeginTransactionAsync(); ... await tx.CommitAsync();), or rely on DB-level ON DELETE CASCADE foreign keys so a single ExecuteDeleteAsync on Campaigns is sufficient. | S |
| NET-02 | `Features/Campaigns/HeroRepository.cs:22` | N+1 query pattern loading each hero's latest snapshot *(downgraded from Important)* | Replace the per-hero LatestSnapshotAsync loop with one query that groups HeroSnapshots by HeroId (e.g. via a correlated subquery in the initial Select, or GroupBy + MaxBy(CreatedAt)) and matches results back to each hero in memory. | M |
| NET-03 | `Features/Campaigns/HeroRepository.cs:87` | Two-step hero deletion not wrapped in a transaction | Wrap both ExecuteDeleteAsync calls in a transaction for consistency with the rest of the delete pipeline. | S |
| NET-04 | `Features/Ingestion/BlockIngestionOrchestrator.cs:55` | DeleteBlocksByHashAsync ignores the caller's CancellationToken | Pass `cancellationToken` instead of `CancellationToken.None`, or add a comment explaining why this specific deletion must always run to completion regardless of cancellation (e.g. to avoid leaving stale+new vectors mixed). | S |
| NET-05 | `Features/Ingestion/EntityExtraction/CanonicalJsonWriter.cs:9` | Per-path SemaphoreSlim locks are never removed or disposed | Either evict/dispose SemaphoreSlim entries once a book's patch workload is idle, use a keyed-lock utility that reference-counts and cleans up automatically, or document that the bounded book count makes this an accepted trade-off. | S |
| NET-06 | `Features/Ingestion/Pdf/StructureBlockExtractor.cs:11` | Sync-over-async blocking on a potentially 2-hour HTTP call *(downgraded from Important)* | Change IPdfBlockExtractor.ExtractBlocks to an async signature (e.g. Task<IReadOnlyList<PdfBlock>> ExtractBlocksAsync or IAsyncEnumerable<PdfBlock>) and await IPdfStructureConverter.ConvertAsync directly instead of blocking with GetAwaiter().GetResult(). | M |
| NET-07 | `Features/Resolution/StructuredFactProjector.cs:24` | Per-table SaveChangesAsync round-trips instead of one batched save | Accumulate all adds/updates/deletes against the single DbContext and call SaveChangesAsync once at the end of ProjectAsync (or once per file section) instead of after every table/choice-set. | S |
| NET-08 | `Features/Retrieval/ModelDownloader.cs:15` | Manual `new HttpClient()` instead of IHttpClientFactory | Inject IHttpClientFactory (or a named/typed HttpClient) into ModelDownloader/CrossEncoderReranker and use CreateClient() instead of `new HttpClient()`. | S |
| NET-09 | `Features/Retrieval/ModelDownloader.cs:33` | Broad exception swallowing hides download-failure root cause | Log the full exception (not just Message) at a higher level for unexpected exception types, and consider surfacing download failures via a health check or startup log summary so operators notice degraded reranking. | S |
| NET-10 | `Features/Retrieval/RetrievalEndpoints.cs:15` | Null-forgiving default (`= default!`) used to satisfy minimal-API parameter ordering | No functional change required; optionally annotate with `[FromServices]` explicitly to make the intent self-documenting and remove reliance on positional-inference plus `default!`. | S |
| NET-11 | `Features/Search/SearXNGClient.cs:34` | Generic exception swallowing in web search returns empty results | Differentiate transport/deserialization failures from genuine zero-result responses (e.g. rethrow or return a result type that signals failure), and log at Error level with the exception object for actionable diagnostics. | S |
| NET-12 | `Infrastructure/Persistence/AppDbContext.cs:62` | JSON-shaped columns mapped as plain `text` instead of native `jsonb` | If these columns are only ever read/written as whole blobs by the app (no server-side JSON queries), the current text mapping is a defensible simplification; if any admin/reporting path will ever need to query into the JSON, migrate to `jsonb` (Npgsql supports `.HasColumnType("jsonb")`) and add a GIN index. | M |

### Simplification (19)

| ID | Location | Finding | Recommendation | Effort |
| --- | --- | --- | --- | --- |
| SIM-01 | `Features/Admin/BooksAdminEndpoints.cs:47` | Misplaced XML doc comment and leftover blank-line gaps from removed methods | Move the summary onto EnqueueOrFailAsync (or delete it) and collapse the stray blank-line runs. | S |
| SIM-02 | `Features/Admin/NeedsReviewService.cs:48` | GetCanonicalFiles re-implements CanonicalSidecarFiles.IsSidecar with a divergent suffix list | Replace the inline EndsWith chain with `CanonicalSidecarFiles.IsSidecar(f)` so all three services share one sidecar definition. | S |
| SIM-03 | `Features/Campaigns/HeroRepository.cs:7` | Stray multi-line blank gaps left after removed types/comments | Delete the extra blank lines. | S |
| SIM-04 | `Features/Entities/CanonicalText/IEntityCanonicalTextRenderer.cs:3` | IEntityCanonicalTextRenderer<TFields> is never used as an abstraction | Either drop the interface from the four typed renderers, or actually dispatch through it (e.g. a Dictionary<EntityType, ...>) to justify keeping it. | S |
| SIM-05 | `Features/Entities/CanonicalText/SimpleEntityRenderers.cs:14` | TagRx / SizeMap / AlignMap / StripTags duplicated across three renderer files | Promote RendererHelpers (or a small shared static) to the single source for StripTags/MapSize/MapAlign and have the Monster and Spell renderers use it. | M |
| SIM-06 | `Features/Entities/CanonicalText/SubclassCanonicalTextRenderer.cs:30` | ExtractFeatureEntry near-duplicated between Subclass and Class renderers | Extract a shared static ExtractFeatureEntry(JsonElement, string objectKey) used by both renderers. | S |
| SIM-07 | `Features/Ingestion/Entities/EntityIngestionOrchestrator.cs:46` | Book-slug derivation one-liner duplicated ~14 times across slices | Extract a single helper (e.g. `EntityIdSlug.BookSlug(IngestionRecord record)` or a `CanonicalPaths` service) and call it everywhere; the canonical-path Path.Combine that usually follows it is a good co-location. | M |
| SIM-08 | `Features/Ingestion/Entities/EntityIngestionOrchestrator.cs:189` | 5etools enrichment + merge-fallback block duplicated between IngestEntitiesAsync and ReindexEntityAsync | Factor the 'enrich + render one envelope' body into a private helper (take the fivetoolsIndex and record, return the finalized EntityEnvelope) and have both methods call it. | M |
| SIM-09 | `Features/Ingestion/Entities/IEntityIngestionOrchestrator.cs:30` | EntityIngestionResult.Enriched is a redundant alias exercised only by a test | Remove the alias and update the test to assert MatchedFivetools, or drop MatchedFivetools in favor of Enriched — keep one name. | S |
| SIM-10 | `Features/Ingestion/EntityExtraction/CanonicalJson.cs:19` | CanonicalJson.ReadOptions is an unreferenced field | Remove ReadOptions, or make the canonical loaders consume it so a single read-options source of truth exists. | S |
| SIM-11 | `Features/Ingestion/EntityExtraction/CanonicalJson.cs:27` | CanonicalJson.WriteAsync is dead code and duplicates CanonicalJsonWriter.WriteAsync | Delete CanonicalJson.WriteAsync; keep only CanonicalJson.WriteOptions (the shared serializer settings), which is the sole member actually consumed. | S |
| SIM-12 | `Features/Ingestion/EntityExtraction/EntityNameMatcher.cs:56` | Match and MatchOfType duplicate the bounded-fuzzy Levenshtein scan | Parameterize a single private scan (optional type filter over EntriesByName) and have both public methods delegate to it. | M |
| SIM-13 | `Features/Ingestion/EntityExtraction/ExtractionDeclinedFile.cs:21` | Near-identical 'write list or delete if empty' logic repeated in three sidecar-file writer classes | Introduce a small shared helper, e.g. a generic `SidecarJsonFileWriter.WriteAsync<T>(path, items, options, ct)`, and have the three record-specific classes call it with their own JsonSerializerOptions. | S |
| SIM-14 | `Features/Ingestion/FivetoolsIngestion/FivetoolsIngestionService.cs:17` | EntityType -> IFivetoolsEntityMapper dictionary duplicated verbatim in two classes *(downgraded from Important)* | Extract the map into a single shared static (e.g. FivetoolsMapperRegistry.Mappers) in Features/Ingestion/FivetoolsIngestion/ and have both classes reference it. | S |
| SIM-15 | `Features/Ingestion/FivetoolsIngestion/FivetoolsMapperBase.cs:9` | Edition2024Sources source-key set duplicated between FivetoolsMapperBase and SpellBackfillService | Move Edition2024Sources to one shared internal static class (or expose it as an internal constant on FivetoolsMapperBase) and reference it from SpellBackfillService instead of redefining it. | S |
| SIM-16 | `Features/Ingestion/Pdf/MinerUPdfConverter.cs:88` | Spell-heading promotion block (normalize/length-check/emit/track) is copy-pasted three times | Factor the block into a local function, e.g. `void TryPromoteHeading(string candidateName)`, and call it from all three call sites. | S |
| SIM-17 | `Features/Retrieval/IReranker.cs:7` | IReranker.SelectTopN is dead in production and duplicates RerankingService's own sort/take logic | Delete SelectTopN from IReranker/CrossEncoderReranker/StubReranker and keep the single implementation in RerankingService.RerankAsync<T>, or conversely have RerankingService delegate to reranker.SelectTopN and drop its own inline sort. | S |
| SIM-18 | `Features/Retrieval/RerankerOptions.cs:9` | RerankerOptions.TopN is an unused configuration property | Remove the TopN property from RerankerOptions, or wire it in as the actual default for finalTopN if that was the original intent. | S |
| SIM-19 | `Features/Retrieval/RetrievalEndpoints.cs:1` | RetrievalEndpoints and EntityRetrievalEndpoints duplicate the same public/diagnostic endpoint-pair scaffolding | Extract a small shared helper (e.g. a generic `MapSearchEndpoints<TQuery,TResult>` or a common base method taking the query-string parse function and the two service delegates) that both endpoint classes call, or accept the duplication as an intentional per-slice cost if vertical-slice isolation is a stated goal. | M |

### Correctness (19)

| ID | Location | Finding | Recommendation | Effort |
| --- | --- | --- | --- | --- |
| COR-01 | `DndMcpAICsharpFun.Tests/Admin/AdminApiKeyMiddlewareTests.cs:10` | Dead and misleading Build() helper is never used and returns a stale flag | Delete the unused Build() helper, or make it return a mutable holder (e.g. a boolean ref/closure the tests can read after InvokeAsync) and have the three tests use it. | S |
| COR-02 | `DndMcpAICsharpFun.Tests/Admin/AdminApiKeyMiddlewareTests.cs:20` | Coverage gap: no test for empty/unconfigured admin API key (potential auth-bypass path) | Add a test asserting that when AdminOptions.ApiKey is null or empty, requests (with and without a header) are rejected 401 — i.e. the middleware fails closed. | S |
| COR-03 | `DndMcpAICsharpFun.Tests/Admin/BooksAdminEndpointsTests.cs:33` | BookSourceRegistry path depends on fragile relative ../../../../ build-output layout | Resolve the repo root via the existing TestPaths helper (used elsewhere in the suite) instead of a hardcoded relative hop count. | S |
| COR-04 | `DndMcpAICsharpFun.Tests/Admin/BooksAdminEndpointsTests.cs:80` | Temp-PDF cleanup runs after asserts (no try/finally) and uses a shared-temp wildcard delete | Use a per-test unique temp subdirectory (Guid) as BooksPath and delete that directory in a finally/IDisposable, instead of a wildcard delete over the shared system temp path. | S |
| COR-05 | `DndMcpAICsharpFun.Tests/Auth/PasswordHasherTests.cs:9` | Coverage gap: PasswordHasher.Verify error paths (malformed/empty stored hash) untested | Add tests asserting Verify(password, "") and Verify(password, "garbage-not-a-valid-hash") return false without throwing. | S |
| COR-06 | `DndMcpAICsharpFun.Tests/Chat/ChatRateLimiterTests.cs:6` | Coverage gap: ChatRateLimiter concurrent-access / shared-state behavior untested | Add a test that fires N parallel TryAcquire calls for one IP against a limit < N and asserts the number of trues equals the limit exactly. | M |
| COR-07 | `DndMcpAICsharpFun.Tests/Chat/DndChatServiceTests.cs:30` | CreatePersonaProvider leaks a temp directory per test invocation | Track created temp dirs and delete them in an IDisposable/IAsyncLifetime teardown, mirroring PersonaProviderTests.Dispose. | S |
| COR-08 | `DndMcpAICsharpFun.Tests/Ingestion/IngestionQueueWorkerTests.cs:25` | Queue-worker test relies on a fixed Task.Delay(150) to observe async dispatch | Signal a TaskCompletionSource from the NSubstitute IngestBlocksAsync stub and await it (with a generous timeout) instead of a fixed Task.Delay. | S |
| COR-09 | `DndMcpAICsharpFun.Tests/Mcp/ResolveCharacterFeatureToolTests.cs:117` | Weak substring assertion Contain("15") can pass on unrelated content | Assert a discriminating string such as "DC 15" (as the sibling integration test CharacterResolutionIntegrationTests already does) or parse the JSON and assert the specific field value. | S |
| COR-10 | `DndMcpAICsharpFun.Tests/Retrieval/ModelDownloaderTests.cs:15` | Unit test makes a real TCP connection to localhost:19999 for the failure path | Inject an HttpMessageHandler/IHttpClientFactory test double that deterministically fails, rather than depending on a live refused connection to a hardcoded port. | M |
| COR-11 | `Dockerfile:4` | Restore layer cache defeated by copying full source before restore | Copy only the csproj, run `dotnet restore`, then `COPY . .` and publish `--no-restore` so restore is cached until dependencies change. | S |
| COR-13 | `Features/Entities/CanonicalText/MonsterCanonicalTextRenderer.cs:84` | Ability-score line emitted with blank values when only some scores present | Render each ability only when present (with a placeholder like '-' for missing), or skip the line unless all six are set. | S |
| COR-14 | `Features/Ingestion/Bm25Vectorizer.cs:9` | Tokenizer discards digits, dropping numeric keyword terms | Use `char.IsLetterOrDigit` for token membership so alphanumeric terms survive tokenization. | S |
| COR-18 | `Features/Ingestion/BookDeletionService.cs:33` | Canonical JSON deletion keyed by slug can delete another book's file | Only delete the canonical file when no other ingestion record resolves to the same slug, or key canonical files by book id. | M |
| COR-19 | `Features/Ingestion/Pdf/MinerUPdfConverter.cs:56` | MinerU response parsing throws opaque exceptions when the results object is empty or the shape differs | Use TryGetProperty and check EnumerateObject().Any() before First(); throw a descriptive InvalidOperationException naming the file and the missing field on malformed responses. | S |
| COR-20 | `Features/Resolution/CharacterResolutionService.cs:152` | saveDC component cites the saveAbility cell's provenance for a value that is computed, not sourced | Attach the tier/rules provenance (or an explicit synthetic 'computed: 8 + PB + CON' marker / null) to the saveDC component rather than the saveAbility cell's provenance. | S |
| COR-21 | `Features/Resolution/StructuredFactProjector.cs:30` | Re-projection leaves orphaned tables and choice-sets that were removed from the canonical file | Scope a delete of tables/choice-sets for the file's SourceBook that are not in the current file (or delete-all-for-book then re-insert) so removals propagate. | M |
| COR-22 | `dnd-mcp-api.insomnia.json:1` | Insomnia collection is missing the registered /admin/canonical/normalize endpoint | Add a request for POST /admin/canonical/normalize (with the X-Admin-Api-Key header) to the Admin — Books or a Canonical group in the collection. | S |
| COR-25 | `docker-compose.yml:36` | App healthcheck only probes the TCP port, not the /health or /ready readiness endpoint | Point the healthcheck at `/health` (liveness) or `/ready` (readiness) via wget/curl instead of a bare TCP connect. | S |

## Coverage

- **Manifest:** 368 files across `Features/`, `Domain/`, `Infrastructure/`,
  `CompanionUI/`, `Program.cs`, `Tools/`, tests, Docker/compose, and the
  `Directory.Build.*` / `.http` / insomnia contract files.
- **Method:** 5 dimensions split into 15 agent chunks (36–60 files each), each
  audited end-to-end via Serena symbol reads — no sampling.
- **Ledger reconciliation:** the union of every agent's `visited` ledger
  equals the manifest exactly — **368/368 covered, 0 missing, 0 skipped**, so
  no re-dispatch was needed.
- **Verification:** all 21 Critical/Important candidates went through an
  adversarial refute pass (15 CONFIRMED, 6 DOWNGRADED, 0 REFUTED); 10 random
  Minor findings were spot-checked against live code.

> **ID note:** finding IDs are stable per-dimension identifiers assigned in
> file order across all severities, so the Critical/Important lists skip
> numbers that belong to Minor findings (e.g. `SEC-01`, `SEC-05`, `SEC-06` are
> Minor). Two BM25 hashing findings (`COR-16`, `COR-17`) describe the same
> defect from different lines and are both retained.
