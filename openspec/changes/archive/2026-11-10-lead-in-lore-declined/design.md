## Decisions

- **Where:** `OverrideMerger.SetDisposition`, the single place a programmatic disposition is derived, so new and existing
  books get the rule without re-extraction (same rollout as `entry-level-advisory`).
- **Match:** type `Lore`; `fields.entries` is exactly one string; at most one sentence end (`.`, `!`, `?` followed by space or
  end; a colon does not end a sentence); and a forward-pointing phrase starts within the first 60 characters (the following, here are, here's what you need to know,
  sections that follow, this section/chapter presents…, are/is presented, presents a/one/…, use the … table(s), the …
  table offers/describes/…, roll on the) or the back-cover phrase "the world's greatest roleplaying game" appears anywhere.
- Declined wins over checks and approvals: there is nothing to approve.
- /review lists `Declined` as not flagged.
- Tuned on all 161 one-sentence Lore entities of the 16 books: 45 match, all pure introductions or blurbs (borderline:
  `erlw.lore.falling`, a pointer to a table elsewhere). One-sentence entities that state a rule (`dmg14.lore.boon-of-high-magic`)
  do not match, nor does a rule using "the following" mid-sentence (`tce.lore.resistantarmor`).
