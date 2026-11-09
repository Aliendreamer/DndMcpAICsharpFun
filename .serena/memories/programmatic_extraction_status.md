Programmatic extraction (Tools/BookExtractor, no LLM) — status 2026-11-08. Supersedes extraction detail in companion_roadmap.

- Phase 1/2a/2b/2c ALL SHIPPED + archived (2c = `2026-11-08-programmatic-extraction-phase2c`: God, Rule, Plane, Faction, Location, Lore).
- Live run of all 16 books committed d2cf29c4: dnd_entities per book == IngestionRecords.EntityCount == accepted count; no legacy LLM points.
- Totals: 4137 Accepted, 4832 NeedsReview. Lore is 4196 of the NR (round 4 split every printed sub-heading into its own Lore entity; strict D4). Excluding Lore ~86% Accepted. Owner OK with Lore staying NR (it remains in dnd_blocks prose retrieval).
- Accepted wrong-rate estimate after final fixes: ~1.6% of the 551 new 2c entities before the residual-flag commit a1601767 (which demoted the known classes); earlier types unchanged.
- Section recall (honest scorer) >=90% only SCAG; others 60-89% (unread caps/table/card headings; PHB bold lead-ins by design).
- Follow-ups: review queue (bulk-approve by reason), C# typed God/Faction/Location/PlaneFields records don't match programmatic keys (renderers read raw JSON), stale ManualCasesTests OpportunityAttack, MM variant field, stale FeatFields/BackgroundFields.
- Then the user's 3 items: /review queue (IsAdmin, approve Wizard/glued-name subclasses/DMG NPC races), chat coach 100s HttpClient timeout, Phase 1 gaps (24 MM monsters, item misses).
- Cost lesson: subagents on sonnet/haiku, Serena for code, capped corpus reruns.
