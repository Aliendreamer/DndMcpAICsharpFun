## Why

The MM/PHB/DMG extraction runs validated the content-first honesty pipeline but surfaced
two problems. First, the "is this a stat block?" notion is reimplemented in four places
(`StatBlockScanner`, `StatBlockSignature`, `ExtractionCandidateDeduplicator.HasStatBlock`,
and the grounding usage), each matching "Armor Class" / "Hit Points" by hand — drift waiting
to happen. Second, the deterministic Monster override (added mid-session, un-specced) is the
only deterministic type rule, it misfires on tutorial/fragment content (the DMG "Creating a
Monster" chapter produced garbage Monsters like "Step 2. Basic Statistics" and
"Challenge 7 (2,900 XP)"), and the same class of fix is needed for magic items (Vorpal Sword
typed `Item` instead of `MagicItem`). Both problems share one root: there is no single,
tested place that answers "what does this text/name look like?" and no single ladder that
turns that answer into a type decision.

## What Changes

- Add `ExtractionSignatures` — one tested utility for content/name recognition: token
  primitives (`HasArmorClass`/`HasHitPoints`/`HasChallenge`/`IsSizeTypeLine`),
  `IsCompleteStatBlock` (the Monster signature), `IsMagicItem` (rarity / "requires
  attunement" / "wondrous item" — the MagicItem signature), and `IsEntityLikeName`
  (false for headings/fragments like "ACTIONS", "Appendix D", "Step 2…", "Challenge 7 (… XP)").
- Route the orchestrator's per-candidate type decision through one deterministic
  `ResolveDeterministicType` ladder: drop non-entity-named → force **Monster** on a complete
  stat block **with a creature-like name** → force **MagicItem** on a magic-item signature →
  else fall through to the content-first union (unchanged).
- Consolidate the four duplicated stat-block checks onto `ExtractionSignatures`; fold
  `StatBlockSignature` into it. `StatBlockScanner` keeps its candidate-detection role but
  stops hand-matching "Armor Class".
- **Behavior fix 1:** the creature-like-name guard stops the Monster override firing on
  tutorial/fragment content.
- **Behavior fix 2:** non-entity-named candidates are dropped before extraction (no wasted
  LLM call, no garbage entity).
- **Behavior fix 3:** magic items resolve to `MagicItem` deterministically.

## Capabilities

### New Capabilities
- `deterministic-type-resolution`: a per-candidate, pre-union step that recognises content
  and name signatures (`ExtractionSignatures`) and deterministically drops a candidate or
  forces its type (Monster, MagicItem) before falling through to content-first extraction.

### Modified Capabilities
<!-- None: the content-first union behaviour is unchanged for candidates that reach it; the
     new capability sits in front of it and composes with it. -->

## Impact

- New: `Features/Ingestion/EntityExtraction/ExtractionSignatures.cs` (+ tests).
- Modified: `EntityExtractionOrchestrator` (`ExtractOneAsync` routes through
  `ResolveDeterministicType`; the inline Monster override is replaced),
  `ExtractionCandidateDeduplicator` and `StatBlockScanner` (use shared primitives),
  candidate building (drop non-entity-named).
- Removed/folded: `StatBlockSignature` merges into `ExtractionSignatures`.
- Behaviour-preserving for the consolidation (existing 676 non-persistence tests stay green);
  the three fixes change output and are validated by a live re-run of MM + PHB + DMG.
- No API/endpoint, schema, or persistence changes.
