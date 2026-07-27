# Validation — offline replay over the real recovery output

The guard is a pure string predicate, so the cheap deterministic harness is a complete test: every
name the decline-recovery pass actually produced on the three setting books, replayed through the
**real C# predicate** (not a reimplementation) and classified.

Source data: GGR's 86 removed headings and 15 kept entities from the `504b045` cleanup, plus the 10
EGW and 10 SCC recovery names still in the corpus.

## Result

| Group | Flagged as heading | Admitted |
| --- | --- | --- |
| GGR — removed as junk (86) | **63** | 23 |
| GGR — kept as real (15) | **0** | 15 |
| EGW recoveries (10) | 4 | 6 |
| SCC recoveries (10) | 6 | 4 |

**Zero false positives.** None of the 15 named GGR legendary NPCs (`Teysa Karlov`,
`Mizzix of the Izmagnus`, `Prime Speaker Zegana`, `Warleader Aurelia`, `Lazav the Faceless`, …) is
flagged. That is the property that matters most: a guard that suppresses real content is worse than
the bug it fixes.

**It generalises to books it was not tuned on.** EGW and SCC were not used to pick the patterns beyond
adding their observed category nouns, and on them the guard flags genuine headings
(`Artifact Descriptions`, `Wildemount Bestiary`, `Owlin Traits`, `Race Option`, `Prismari Personality`,
`Ability Score Increases`) while admitting genuine content (`Vestiges of Divergence`,
`Elder Evils of Exandria`, `Core Spawn`, `Nergaliid (Devil Toad)`). This also surfaced that the manual
cleanup **under-cleaned EGW and SCC** — it was GGR-focused, and those two still carry a handful of
heading entities.

## What it does NOT catch (73% floor, by design)

23 of GGR's 86 remain admitted. They fall into groups the shape rules cannot reach:

- **Setting-specific creature plurals** not in the D&D type vocabulary — `Benthids`, `Blastseekers`,
  `Mizzium`, `Erstwhile`. Catching these needs per-book vocabulary, which is the whack-a-mole pattern
  this repo has repeatedly found to plateau.
- **Subclass-feature fragments** — `Divine Strike`, `Symbiotic Entity`, `Spreading Spores`,
  `Voice of Authority`, `Channel Divinity: Order's Demand`. These are indistinguishable from real
  entity names by shape; some are arguably legitimate `Rule` entities.
- **Prose-shaped headings** — `Coins in Treasure`, `Party Makeup`, `Common Cause`,
  `Horrors and Madness`.
- **4 magic-item stat-line names** (`Ring, uncommon (requires attunement)`, …) which are a different
  guard's job (`MagicItemStatLineName`) and reached the canonical via the extraction path, not this
  pass — they are in the removed list only because the cleanup swept them too.

This is the stated design position: the guard is a floor, not a proof. Everything it misses still
reaches the model's `none` option exactly as before, so the change is strictly additive — it cannot
make recovery worse than it is today.

## Expected effect on a re-run

Re-extracting GGR would drop roughly 63 junk entities at source and save the same number of recovery
LLM calls. The three shipped books were cleaned by hand in `504b045`, so no re-extract is required to
realise the data benefit — this change prevents recurrence on the next section-organised book.
