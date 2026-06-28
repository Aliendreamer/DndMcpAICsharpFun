# Finishing a spec — commit → archive → optimize skills (standing dev-flow rule)

User rule (2026-06-28, updated): when a spec/change is finished AND the user says "commit", run this finish sequence as part of the development flow:

## 1. Commit
Commit the finished work on `main` (single-dev, no branches — `mem:workflow/work_on_main`).

## 2. Archive the active spec
- Check for an active openspec change: `openspec list`.
- Archive it: `openspec archive <name> -y` (or the `opsx:archive` / `openspec-archive-change` skill) — natively syncs the delta specs into `openspec/specs/`. See `mem:project_openspec_archive_cli` (MODIFIED deltas must match main-spec headers verbatim).
- Commit the archive.

## 3. Optimize skills
- After archiving, run the **`skill-optimizer`** skill to optimize our skills (activation, clarity, cross-model reliability, regression/benchmark gates).
- (This replaces the earlier `superpowers:writing-skills` choice — the user installed `skill-optimizer` specifically for this step, 2026-06-28.)

## Notes
- Order is COMMIT → ARCHIVE → SKILL-OPTIMIZER.
- This sits at the END of the standing workflow `mem:workflow/skill_chain` (brainstorm → propose → writing-plans → subagent-driven-development → **finish: commit → archive → skill-optimizer**).
- Do NOT archive while a spec's acceptance gate (e.g. a live validation run) is still pending — archive once it is actually finished/validated.
- Currently pending archive (once their live PHB validation passes): `extraction-name-resolution`, `extraction-authoritative-allowlist`.
