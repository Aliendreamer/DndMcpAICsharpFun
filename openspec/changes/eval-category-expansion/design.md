## Context

`Tools/ModelEval --mode full` (change `full-chain-eval`) scores cases through `POST /admin/eval/turn` and attributes
failures (`Attribution.Score`): wrong-tool → bind-error → wrong-tool-data → wrong-answer. `Attribution` is generic over
`EvalCase(Id, Category, Tag, Prompt, ExpectedTools, ToolDataFacts?, AnswerFacts)`, so new categories need cases, not new
scoring. Owner decisions for this change (2026-11-03/04 conversation): answers must come from data; when the data lacks
the answer the chat must say it couldn't find it; add rules, lore, monster and NPC cases now; NPC quality is not judged;
the owner set the goal "finish the eval changes tonight, context work tomorrow" — so no router/persona changes here.

## Goals / Non-Goals

**Goals:** monster, rules, lore, npc and not-found categories; per-run reply text + called tools in the report; a new
committed live baseline.

**Non-Goals:** fixing routing/persona/retrieval (next change); an LLM judge; NPC creative quality; per-campaign lore
seeding.

## Decisions

### D1 — Monster generator over `mm14`

Eligible: `type == "Monster"`, `needsReview != true`, and the asked field present and renderable. Templates (prompt names
"the 2014 Monster Manual"):

- armor class → `fields.ac` (first entry's `ac` when it is an object list, or the integer);
- hit points → `fields.hp.average`;
- challenge rating → `fields.cr` (string, e.g. `"1/2"`, `"5"`).

Expected tools = the class/spell set (`get_entity`, `search_entities`, `search_lore`, `search_dnd`). Tool-data variants
are the raw JSON forms (`"ac":16`, `"average":27`, `"cr":"5"`) plus the answer variants (same rule as ruling 5 of the
previous change). Answer variants are unit-qualified (e.g. `armor class 16`, `ac 16`, `27 hit points`, `challenge
rating 5`, `cr 5`) — never a bare number. Ids `gen.monster.<entityId>.<template>`. Seeded with the existing seed and per
category count. `mm14` fields are extraction-owned (not 5etools-filled), which is accepted: a wrong canonical value shows
up as `wrong-tool-data`, a true finding.

### D2 — Default books `phb14,mm14`

Each generator only reads entities of its own type, so adding `mm14` affects only monsters. The case-set hash changes;
the comparison against the old baseline covers shared ids (existing behaviour).

### D3 — Manual cases for rules, lore, npc, not-found

- **rules** (5): prone (advantage within 5 ft), grappled (speed 0), opportunity attack (leaving reach), Dodge
  (disadvantage), half cover (+2). Expected tools `ask_rules`, `search_lore`, `search_dnd`.
- **lore** (4, Eberron): Last War, Cyre/Mourning, Sharn, warforged. Expected tools `ask_setting_lore`, `search_lore`,
  `search_dnd` — `ask_setting_lore` needs a campaign id the eval user may not have, so general lore search is accepted
  (my call).
- **npc** (2): expected tools `generate_npc`, `generate_npc_party`; tool-data fact = the archetype was found in the corpus
  (the exact serialized form is confirmed against the service's result type); answer fact = a mechanical stat is
  reported (`hit points` / `armor class` / `hp`). Quality is not judged.
- **not-found** (2): questions about a made-up spell and a made-up monster. Expected tools = entity + lore set; tool-data
  check skipped (`null`); answer variants express "couldn't find" (`couldn't find`, `could not find`, `couldnt find`,
  `not find`, `no record`, `not found`, `don't have`, `do not have`).

Manual-case validation accepts these categories; ids are `manual.<category>.<slug>`.

### D4 — Report run detail

Each case's runs record the called tool names and the reply text truncated to 600 characters. Existing report fields
and baseline comparison are unchanged; old reports without the field still load.

### D5 — Baseline run

Run the expanded set live on the current `main` (router/persona unchanged), commit the report, and record per-category
results and notable failures in `report.md`. The overnight run is OK'd by the owner (asleep, GPU free).

## Risks / Trade-offs

- [Rules/lore answer facts are short words like "advantage"] → each case chooses a word that only a correct answer
  contains; accepted looseness, documented per case.
- [Not-found pass can't prove nothing was invented] → accepted; the phrase check catches the common confident-fabrication
  failure.
- [Run length ~34 cases × 3] → overnight; `--categories` allows subsets.
