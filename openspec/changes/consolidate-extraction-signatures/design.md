## Context

The content-first extraction pipeline (`content-first-extraction`,
`discriminated-union-extraction-decoding`, `extraction-disposition`) was validated this session
on the Monster Manual, Player's Handbook, and Dungeon Master's Guide. Two things became clear:

1. **Duplicated stat-block detection.** "Has a stat block" is hand-matched in four places —
   `StatBlockScanner` (size/type line + "Armor Class"), `StatBlockSignature`
   (AC + HP + Challenge), `ExtractionCandidateDeduplicator.HasStatBlock` (AC + HP), and the
   grounding usage. Four copies of the same string-matching, free to drift.
2. **One un-specced deterministic rule that misfires + a missing sibling.** The Monster
   stat-block override (force `Monster` when text has AC+HP+Challenge) was added mid-session
   without a spec. On the DMG it fired on the "Creating a Monster" tutorial (which literally
   prints an example stat block), producing garbage Monsters named "Step 2. Basic Statistics"
   and "Challenge 7 (2,900 XP)". And magic items need the same deterministic treatment
   (Vorpal Sword was typed `Item`, not `MagicItem`).

Both reduce to: there is no single tested answer to "what does this text/name look like?", and
no single ladder from that answer to a type decision.

## Goals / Non-Goals

**Goals:**
- One tested utility (`ExtractionSignatures`) owning all content/name recognition.
- One deterministic `ResolveDeterministicType` ladder in the orchestrator; `ExtractOneAsync`
  reads as drop / forced-type / union instead of inline branches.
- Fix the three run-surfaced behaviours: override misfire guard, drop non-entity-named
  candidates, magic-item → `MagicItem`.
- Consolidation stays behaviour-preserving for everything except the three intended fixes
  (existing 676 non-persistence tests remain green).

**Non-Goals:**
- The high decline rate / precision-vs-recall tuning (a separate concern).
- Model-level branch-selection accuracy beyond the deterministic magic-item rule (no prompt
  tuning in this change).
- The Marker-conversion headerless-giant ceiling (unchanged).
- Any schema, endpoint, or persistence change.

## Decisions

**1. `ExtractionSignatures` is a pure static utility.** Token primitives
(`HasArmorClass`/`HasHitPoints`/`HasChallenge`/`IsSizeTypeLine`), plus `IsCompleteStatBlock`
(AC+HP+Challenge — the Monster signature), `IsMagicItem` (rarity / "requires attunement" /
"wondrous item"), and `IsEntityLikeName`. All OCR-tolerant case-insensitive `Contains`, matching
the existing `StatBlockSignature` style so the consolidation is behaviour-preserving.
`StatBlockSignature` folds into it; `ExtractionCandidateDeduplicator.HasStatBlock` and the
`StatBlockScanner` token checks call it.

**2. `IsEntityLikeName` is conservative (false-positives over-keep).** It rejects only clear
non-entities: all-caps single words ("ACTIONS"), known headings ("Appendix …"), "Step N …",
and fragments that are mostly a "Challenge X (… XP)" line or otherwise non-name-like (leading
digit, no letters). When unsure it returns true — we would rather keep a borderline candidate
(it can still decline at the union) than drop a real entity. This is the safety property the
user asked for.

**3. `ResolveDeterministicType(candidate)` returns a small result: Drop, or a forced
`EntityType`, or Defer (union).** The ladder order is drop → Monster → MagicItem → defer.
The Monster branch ANDs `IsCompleteStatBlock` with `IsEntityLikeName` (the guard) — so the
tutorial fragments drop at step 1 and never reach the Monster branch. `ExtractOneAsync`
becomes: resolve → (Drop: skip) / (forced: extract with that type's schema, no decline) /
(Defer: existing union path). The current inline Monster-override block is deleted.

**4. Dropping happens in the resolver, not a separate pre-filter.** Keeping the drop decision
inside the same ladder (rather than filtering candidates earlier) keeps one place that decides a
candidate's fate and one place to test. Dropped candidates are simply skipped (not written as
errors) — they are noise, not failures.

**5. `MagicItem` reuses the forced-type extraction path** built for the Monster override
(extract with the type's schema directly, then ground + dispose via `BuildTypedEnvelope`). No
new envelope logic.

## Risks / Trade-offs

- **`IsEntityLikeName` could drop a real entity.** Mitigated by the conservative bias (reject
  only clear headings/fragments) and the live MM+PHB+DMG re-run that compares entity counts
  before/after; any real entity lost shows up as a regression in the known-good lists.
- **`IsMagicItem` could over-fire on a non-item that mentions a rarity word.** Low: the signature
  needs item-specific phrasing ("requires attunement"/"wondrous item") or a rarity term in an
  item context; validated by the DMG re-run (magic items → MagicItem, no spell/class mistyped).
- **Consolidation regression risk.** Mitigated by TDD + the unchanged 676-test suite acting as
  the behaviour-preserving guard, plus the three new behaviours covered by new tests before the
  live re-run.
