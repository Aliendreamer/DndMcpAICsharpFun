## Context

Entity extraction scans a book into candidate section-headers, then admits/declines each candidate before any LLM call. Two deterministic gates in `ExtractionSignatures` decide admission for a gated-prior, no-5etools-match candidate:

- `IsRealEntity(candidate.Text)` — the candidate's TEXT proves a real entity via a structural signature (complete stat block = Armor Class + Hit Points + Challenge; object stat block; magic item; subclass-feature progression).
- `IsEntityLikeName(candidate.Name)` — the candidate's NAME denotes an entity, not a section heading / fragment. Conservative: rejects only clear non-entities (StepHeading, ChallengeFragment, SectionHeading, `Appendix*`, `StructuralHeaders`, `"A … LAIR"`), returns true when unsure.

T3 live validation (keyless MTF/MPMM) showed the two gates can BOTH pass for a **stat-block fragment**: its text sits inside a real stat block (so `IsRealEntity` is true), and its name is not caught by any current `IsEntityLikeName` reject, so the fragment is admitted and extracted as a `Monster`. Because the book was keyless with no 5etools match, it flowed to the web referee and surfaced as `homebrew` — visible noise. Official books have the same gap but the leaks would land as `canon-unindexed`, hiding the defect.

Leaked examples (all real, from `mpmm-keyless`): `"Damage Immunities poison"`, `"AN ANARCH's LAIR"`, `"Effects of the Mold"`, `"Telepathic Torment"`, `"Variant: Chromatic Drakes"`, `"Armor Class 14 (natural armor) Hit Points 71 (13d8 + 13) Speed 30 ft."`.

## Goals / Non-Goals

**Goals:**

- A candidate whose NAME is a stat-block field label or a sidebar/section heading is DECLINED (lands in `.declined.json`), regardless of whether its surrounding TEXT carries a stat-block signature.
- Preserve every real entity currently extracted (Tortle, Babau, Archdruid, all real subclasses/monsters) — no recall regression.
- Lock the fix with a regression fixture of the known-bad + known-good names.

**Non-Goals:**

- Changing the T3 web referee, retrieval, or any HTTP endpoint (this is upstream of all of them).
- Re-architecting the candidate scanner (the deeper root cause is *investigated* and noted; a full scanner rewrite is out of scope for this change unless the name-gate proves insufficient).
- Changing `IsRealEntity`'s text-signature semantics (the authority-ladder tightening stays as-is).

## Decisions

**D1 — Primary fix: tighten `IsEntityLikeName` (cheap, high-value, last-line filter).** Add reject patterns:
- **Stat-block field labels** — a `StatBlockFieldLabel` regex rejecting names that BEGIN with `Armor Class`, `Hit Points`, `Speed`, `Saving Throws`, `Skills`, `Senses`, `Languages`, `Challenge`, `Damage (Immunities|Resistances|Vulnerabilities)`, `Condition Immunities`, or a bare ability-score line (`STR DEX CON …`). These are stat-block *lines*, never entity names.
- **Broadened lair heading** — reject any `"<X> LAIR"` heading, not only the `"A "`-prefixed form (so `"AN ANARCH's LAIR"` is caught). Keep it anchored to end-with-LAIR to avoid rejecting a real creature named "…lair…".
- **Sidebar/section headings** — reject `"Effects of …"`, `"Variant: …"` (and similar `"Variant …"`), which are optional-rule sidebars, not entities.

Keep the conservative default (unknown → true). Every new reject is a *specific* pattern, not a broad heuristic, to avoid dropping real entities.

**D2 — Secondary: investigate the scanner root cause, don't necessarily fix it here.** These fragments should never have been emitted as candidate headers. Document where the scanner/segmentation (`EntityCandidateScanner` + bookmark/MinerU TOC) produces them; only widen scope to a scanner fix if the name-gate patterns prove too whack-a-mole. The name gate is the reliable backstop either way.

**D3 — Regression guard via a fixture, not a live re-extract.** Encode the known-bad names (must decline) and known-good names (must admit) as a unit-test fixture against `IsEntityLikeName`, so the guard runs in CI without Docker/Ollama. A live keyless re-extract of MTF/MPMM is an optional manual confirmation, not the gate.

*Alternative rejected:* raising `IsRealEntity`'s bar (e.g. require the stat-block name line to match the candidate name) — more invasive, risks the authority-ladder recall wins, and doesn't address heading-style leaks with no stat block ("Effects of the Mold").

## Risks / Trade-offs

- **Over-rejection (dropping a real entity)** → Mitigation: each pattern is specific and anchored; the fixture asserts known-good names still admit; patterns match stat-block *field labels* / sidebar phrasings that cannot be legitimate entity names.
- **Whack-a-mole (new fragment shapes leak later)** → Mitigation: the fixture makes regressions loud; if leaks recur across shapes, escalate to the D2 scanner fix.
- **Official-book silence** → the same gap exists on official books (lands as `canon-unindexed`); the fix improves them too, but the visible evidence came from keyless — validate there.
