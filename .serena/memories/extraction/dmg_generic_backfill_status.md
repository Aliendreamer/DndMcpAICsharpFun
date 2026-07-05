# DMG extraction + Object type — DONE + ARCHIVED (2026-07-05)

Two openspec changes shipped + archived for DMG:
- `dmg-generic-backfill` (archived 2026-07-04): generic EntityBackfillService + 4 providers +
  type-parameterized routes. New capability `fivetools-entity-backfill`.
- `object-entity-type-and-decline-leak` (archived 2026-07-05): new Object entity type + decline-not-leak.

## Object type — the hard-won result
Goal: siege weapons (ballista/cannon) extract as a real `Object` type with AC/HP/attack, NOT empty
Item shells with the model's reasoning leaked into canonicalText. Took FOUR fixes across two re-extracts:
1. **union-hoist** (739b5ef): ObjectFields was the only schema with `$ref`/`definitions`; the
   ExtractionUnionSchemaBuilder dropped branch `definitions`, so `#/definitions/ObjectHp` dangled in
   the union -> Ollama HTTP 400 "json_schema conversion failed" on EVERY stat-block candidate (~70%
   mass failure). Fix: hoist each branch's definitions to the union root. ObjectFields now uses its
   OWN ObjectHp/ObjectAttack sub-types (not shared MonsterHp/MonsterBlock).
2. **decline-not-leak** (bf08d13): UnionOutcome.Declined -> ExtractionErrorEntry("extraction_declined"),
   NOT a persisted shell (removed DeclinedEnvelope, which wrote reason into canonicalText).
3. **deterministic Force(Object)** (803da7b): the LLM classified UNRELIABLY (Ballista->Object w/o stats,
   Cannon->Monster). ExtractionSignatures.IsObjectStatBlock ("<Size> object" + AC + HP, no Challenge)
   -> DeterministicTypeResolver.Force(Object). Now reliable + extracts via the ObjectFields schema.
4. **/no_think** (803da7b): qwen3 thinking caused runaway generations (3793+ tokens) that exhausted the
   token budget -> "Empty response" failures + ~8x slowdown (~114s/candidate). Append `/no_think` to the
   extraction user turn -> ~14-42s/candidate, 0 non-decline losses (was 7). Type selection is already
   deterministic + union-constrained so <think> adds nothing.

## FINAL DMG canonical (committed 62444a9)
Re-extract: 259 clean / 177 errors (mostly declines) -> 1151 after 5etools backfill. Ballista + Cannon
= type=Object with ac/hp/immune/action. 0 reasoning shells. All 1151 ids UNIQUE, canonical/validate 0
failures, 60 NeedsReview flags. Committed + archived.

### Known residuals (hand-correctable, flagged)
- Ballista/Cannon appear TWICE (dmg14.monster.* + dmg14.item.* — distinct ids, both Object): the
  stat-block candidate AND the prose candidate each produced one. Redundant, not load-breaking.
- 2 junk Objects named "Damage Immunities: poison, psychic" (a stat-line fragment mis-scanned as a
  candidate name, then Force(Object)'d). Over-scan artifact; tighten StatBlockScanner naming or
  IsObjectStatBlock later.
- Ram/Cauldron/Mangonel/Trebuchet not captured as Object (declined/absent upstream).

## Infra / follow-ups
GPU healthy (RTX 5070); `mem:project/sandbox_blocks_gpu`. Force(Object)+/no_think shipped in the
extraction code but NOT yet folded into the extraction-think-mode spec tasks (that spec = config toggle).
`qdrant-scalar-quantization` (committed 11c7665) code done; live validation (4.2/4.3) needs a rebuild.
Relates to `mem:companion_roadmap`, `mem:project_entity_extraction_rethink`.
