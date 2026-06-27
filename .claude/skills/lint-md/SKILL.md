---
name: lint-md
description: Lint and auto-fix this repo's hand-authored markdown (README.md, CLAUDE.md, docs/) with markdownlint via pnpm. Use after writing or editing any of those .md files, and before committing markdown changes — a markdown change is not done until it lints with zero errors.
---

# Lint markdown

Conform the repo's hand-authored markdown to markdownlint. Run from the repo root.

## Steps

1. If `node_modules/` is absent, run `pnpm install` first (toolchain is pnpm; the version is pinned by `packageManager` in `package.json`).
2. Auto-fix: `pnpm lint:md:fix`
3. Verify clean: `pnpm lint:md` — it MUST report `0 error(s)`.
4. If any violations remain after auto-fix (some rules can't be auto-fixed), open the flagged file(s) and fix them by hand, then re-run step 3 until clean.

## Scope

- Config: `.markdownlint-cli2.jsonc` (globs `**/*.md` minus the ignores).
- Linted: `README.md`, `CLAUDE.md`, and `docs/**`.
- NOT linted (ignored): `openspec/**` (opsx manages spec format), `node_modules`, `bin`/`obj`, `.git`, `.claude`, `.superpowers`, `.serena`, `5etools`.

## Notes

- Do not "fix" violations by widening the ignore list or disabling rules in the config unless the rule genuinely conflicts with this project's doc style — fix the markdown instead.
- The remaining `js-yaml` audit advisory is intentionally not patched (the patched version drops the default export markdownlint-cli2 imports); see `pnpm-workspace.yaml`.
