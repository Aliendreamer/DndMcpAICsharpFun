## 1. Automated tooling pass

- [ ] 1.1 Generate the coverage manifest (file lists per audit surface) and record the audited commit SHA
- [ ] 1.2 Run `dotnet list package --vulnerable --include-transitive` and capture output
- [ ] 1.3 Run `dotnet build` (warnings-as-errors baseline) and `dotnet format --verify-no-changes`; capture output
- [ ] 1.4 Scripted scans: secret-pattern grep (key/token/password literals outside masked config), anonymous-endpoint markers, endpoint inventory (`MapGet/Post/Put/Delete` + MCP tools) for the auth-posture table

## 2. Parallel dimension audits (agent fan-out, read-only)

- [ ] 2.1 Security audit over the manifest (VPS threat model; endpoint auth classification; secrets handling; ingestion SSRF/path traversal; rate limiting; cookies/headers)
- [ ] 2.2 Structure/architecture audit (slice boundaries, Domain purity, dependency direction, oversized files)
- [ ] 2.3 .NET best-practices audit (async correctness, DI lifetimes, EF Core patterns, nullability, disposal, cancellation)
- [ ] 2.4 Simplification audit (dead code, duplication, over-abstraction, unused packages)
- [ ] 2.5 Correctness + tests + infra audit (logic bugs, races, test smells/gaps on critical paths, Dockerfile/compose hygiene, `.http`/insomnia contract drift)
- [ ] 2.6 Reconcile visited/skipped ledgers against the manifest; re-dispatch any gaps until coverage is complete

## 3. Adversarial verification

- [ ] 3.1 Merge/dedup findings by `file:line`; assign stable IDs and provisional severity
- [ ] 3.2 Refute-pass every Critical/Important finding against live code; drop/downgrade refuted ones, keep refutation notes
- [ ] 3.3 Spot-check Minor findings during synthesis

## 4. Report + finish

- [ ] 4.1 Write `docs/audits/2026-07-02-full-repo-audit.md` (finding format per spec: ID, severity, dimension, file:line, what/why, recommendation, S/M/L effort; endpoint auth-posture table; tooling evidence; commit SHA)
- [ ] 4.2 `pnpm lint:md:fix` + `pnpm lint:md` → 0 errors
- [ ] 4.3 Verify no code/config changes (`git status` clean apart from report + change artifacts); commit the report
- [ ] 4.4 Joint triage session with the user → decide fix batches → spin follow-up changes (out of this change's scope)
