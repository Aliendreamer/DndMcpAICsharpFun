# Expanded full-chain baseline — 2026-11-04

Report: `Tools/ModelEval/reports/20261104-013938-qwen3_8b-full.json` — `qwen3:8b`, seed 42, books `phb14,mm14`, 34
cases × 3 runs, case-set hash `3dc45ef6b8aa`, 0 infra errors. This is the **"before" baseline for the context-engineering
change**: router, persona and retrieval are unchanged since the 2/16 run.

| Category | Passed | Main failure labels (runs) |
| --- | --- | --- |
| class | 2/6 | not-chosen 11 |
| spell | 1/5 | wrong-tool-data 12 |
| crafting | 0/5 | not-offered 15 |
| monster | 3/5 | wrong-tool-data 3, not-chosen 2 |
| rules | 3/5 | wrong-tool-data 3, wrong-answer 2 |
| lore | 0/4 | wrong-tool-data 11, bind-error 1 |
| npc | 2/2 | — |
| not-found | 1/2 | not-chosen 4 |
| **total** | **13/34** | |

## Findings (verified from `runDetails` and direct `/admin/eval/turn` traces)

1. **Crafting is never offered.** `QuerySignals.StructuredLookupSignal` matches "how many" ("how many workweeks"), which
   forces the structured-lookup group with confidence 1.0 and hides `calculate_crafting`.
2. **Fact questions go to the wrong group.** "What level is the spell Fireball" is embedded closest to the
   character-resolution exemplar "what do I get at level 8", so `get_entity` is hidden and only `search_lore` remains. It
   returns class spell lists, not the spell entry (spell wrong-tool-data 12/15).
3. **Class questions: the model skips the offered tool** (not-chosen 11/18) and answers from memory.
4. **Lore: two compounding bugs.** The model calls `ask_setting_lore` with a guessed `campaignId: 1` (not the user's
   Eberron campaign, id 3) and gets "Campaign not found", then calls `search_lore` with `category: "Lore"`, which surfaces
   Fizban's Treasury instead of the Eberron book.
5. **Empty replies.** In earlier runs, "generate one NPC" and the made-up-monster question returned an empty text with no
   tool call in all three runs. In this run the NPC case passed, but the made-up-monster case still skipped tools.
6. **Persona allows memory answers.** `Config/personas/companion.md` rule 4 says "offer your best general guidance" when
   retrieval finds nothing, which contradicts the owner's rule (say it couldn't find it).
7. **Canonical data bug (out of scope, needs its own change):** `books/canonical/mm14.json` disagrees with 5etools for
   CR on 122 of 373 reviewed monsters (e.g. Grell 1/4 vs 3), HP 33/272, AC 21/253. The eval now only asks about values
   that agree with 5etools (design D6); `dnd_entities` still serves the wrong values to the chat.

## Harness bugs fixed during the live runs

Each was found by reading run details and is pinned by a test:

- JSON-escaped `+` / `'` in tool output.
- `hp` matching across word joins.
- CR strings like `10 (5,900 XP)`, and `cr 1` matching `cr 1/2`.
- `advantage` ⊂ `disadvantage`.
- Missing real reply forms: "Challenge Rating (CR) of 3", "hit points as 187", "AC 17, 58 HP", "I don't recognize", "out
  of their reach", "no official spell".
- Monster expectations taken from wrong canonical data.

Remaining accepted limitations: a not-found reply that hedges and then invents still passes, and lore prompts don't name
a campaign id.
