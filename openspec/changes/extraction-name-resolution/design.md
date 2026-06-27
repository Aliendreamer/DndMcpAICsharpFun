## Context

The resolver pipeline (`deterministic-type-resolution`, shipped in `consolidate-extraction-signatures`)
silently drops ~50 real entities. Confirmed root cause: `ExtractionSignatures.IsEntityLikeName`'s
all-caps single-word rule. Marker renders entity names as ALL-CAPS headings (`FIREBALL`, `ABOLETH`),
so the rule meant for `ACTIONS`/`REACTIONS` also nukes real single-word entities; multi-word names
escape via the space. The full 106 MB 5etools mirror is local (`5etools/`) and verified-complete for
the official corpus (361 PHB spells, 3733 monsters across all sources, every class/background) — the
ground truth that fixes recall and name-quality together.

## Goals / Non-Goals

**Goals:**
- Recover the dropped entities (Fireball, Aboleth, Bard, …) with correct types and clean names.
- Keep precision: `ACTIONS`/`REACTIONS`/lair headings stay out (not force-typed Monster).
- Make name+type deterministic for the whole official corpus (no 8B re-guessing of known facts).
- Never wrongly drop a real-but-unlisted entity (safety fallback to content-first).

**Non-Goals:**
- Standalone OCR de-spacing for *unmatched* residual names (deferred — Item 4b).
- Using 5etools for content/fields — extraction stays prose-grounded (5etools sets name+type only).
- Re-typing entities the model already got right via content-first (only matched candidates change).

## Decisions

**1. 5etools matching is a deterministic KEEP/CLEAN/TYPE signal, never a DROP.** A match yields the
canonical name + type and skips the LLM type-decision. A non-match falls through to the existing
content-first union — so a 5etools gap degrades to today's behavior, never worse. This is the safety
property that makes the change low-risk.

**2. Match the FULL 5etools corpus per type, not per book.** PHB appendix animals (Lion, Frog) are
catalogued by 5etools under MM. The index is keyed by name+type across all sources; the entity's
SourceBook stays the book we're extracting.

**3. Index TOP-LEVEL types only.** spell / monster / item (rarity → MagicItem else Item) / class /
background / race / feat / condition / deity→God / plane→Plane. Deliberately exclude
`optionalfeatures` and subclass-features — indexing them would resurrect the feature-noise
(`Spellcasting`, `Archery`, `Metamagic`) the resolver correctly stopped extracting.

**4. Fuzzy match with a confidence threshold.** Exact normalized match first; then fuzzy (token /
Levenshtein) accepted only above a threshold, so `MAGEARMOR`→"Mage Armor" resolves but a wrong
neighbour does not. Below threshold → null → content-first fallback (never a wrong canonical name).

**5. The 5etools step precedes the drop filter.** In `DeterministicTypeResolver`, the 5etools match
is ladder step 1, *before* `IsEntityLikeName`-based dropping — so a known entity is never dropped by
the name filter. The name filter only governs *unmatched* candidates.

**6. `IsEntityLikeName` fix = denylist + lair, not all-caps.** Replace the broad
`n == n.ToUpperInvariant() && !n.Contains(' ')` rejection with an explicit structural-sub-header
denylist (`ACTIONS/REACTIONS/TRAITS/BONUS ACTIONS/LEGENDARY ACTIONS/LAIR ACTIONS/REGIONAL EFFECTS`,
case-insensitive) and a lair-name reject (`A …'s LAIR`). Keeps the stat-block force-Monster guard,
stops nuking real entities.

**7. Index load cost.** 5etools is 106 MB but we only need name+type+source per entity. Build a
compact index (singleton, once at startup) holding only those — not the full entity JSON.

## Risks / Trade-offs

- **Fuzzy mis-match** (wrong canonical name) → mitigated by the confidence threshold + the no-drop
  safety (a borderline candidate falls to content-first rather than getting a wrong name).
- **5etools coverage gap for an official entity** → the unmatched entity uses content-first (extracts
  + accepts/declines as today). Recall floor = today; matching only raises it.
- **Denylist incompleteness** (a structural sub-header not on the list) → it becomes an unmatched
  candidate → content-first declines it (noise, an LLM call, never fabrication). Acceptable.
- **Index staleness** — the local 5etools snapshot is a point-in-time mirror; new official entities
  need a refreshed dump. Out of scope here; the content-first fallback covers anything missing.
- **Validation cost** — re-running 4 books is hours (the PHB tail crawls). Unavoidable to confirm
  recall recovers without regressing precision; it is the acceptance gate.
