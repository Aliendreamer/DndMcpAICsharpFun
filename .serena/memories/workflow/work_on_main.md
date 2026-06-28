# Work on main — no branches (single-dev)

User instruction (2026-06-28): "we always work on master / main it is single dev project / i don't have time for branches etc."

## Rules
- **Work DIRECTLY on `main`.** Do NOT create feature branches or worktrees for changes.
- **Commit autonomy granted** (also "i let you work without observation" / "don't ask me for each commit"). Commit reviewed work straight to main; don't gate behind a branch diff or ask per-commit.
- Still run the quality workflow `mem:workflow/skill_chain` (brainstorming → opsx:propose → writing-plans → subagent-driven-development with per-task TDD + reviewer subagents) — it caught a critical design bug (dead allowlist gate) — but EXECUTE IT ON main, committing each reviewed task to main instead of a branch.
- Destructive git stays denied (`git push --force/-f`, `git reset --hard`, `git branch -D`, `rm -rf`). Don't push to a remote unless asked.
- Keep only `main`: delete stray merged branches with `git branch -d`.

## Memory storage preference
**Write project memories with Serena (`write_memory`), here under `.serena/memories/`** — NOT the `~/.claude/.../memory/*.md` file system. Use the existing `workflow/<name>` topic convention. (The user corrected this explicitly 2026-06-28.)
