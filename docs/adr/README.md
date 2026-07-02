# Architecture Decision Records

This directory records architectural decisions that are not obvious from the code
alone — in particular, couplings and configuration boundaries that a reader might
otherwise flag as accidental. Each ADR states the context, the decision, and its
consequences so the intent survives future refactors.

These three ADRs capture design characteristics surfaced by the 2026-07-02 full-repo
audit (`docs/audits/2026-07-02-full-repo-audit.md`) that were reviewed and accepted as
intentional rather than changed:

- [0001 — Admin is a cross-cutting orchestration slice](0001-admin-is-a-cross-cutting-orchestration-slice.md) (audit STR-03)
- [0002 — The canonical directory is configured per pipeline phase](0002-canonical-directory-configured-per-pipeline-phase.md) (audit STR-05)
- [0003 — Ingestion depends on Entities as a shared kernel](0003-ingestion-depends-on-entities-as-a-shared-kernel.md) (audit STR-07)
