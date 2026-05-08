# 5etools Schema Alignment — Design

**Goal:** Align all canonical entity schemas to the 5etools JSON format so that 5etools data can be ingested without transformation, and LLM extraction outputs the same format.

**Scope:** Schema files + extraction prompts only. No C# application code changes (fields are stored as opaque `JsonElement`). A separate spec covers the 5etools ingestion pipeline.

---

## Why

5etools has all official D&D 5e content in structured JSON. Our LLM extraction pipeline stalls on large table pages (token limit) and is slow (~20–65 s/candidate). If our canonical schema matches 5etools format, we can ingest their data directly with no transformer — the LLM pipeline becomes a supplement for content not in 5etools (homebrew, uncovered books, hand corrections).

---

## Core Conventions Adopted from 5etools

All schemas switch to these formats:

| Concept | Old (ours) | New (5etools) |
|---|---|---|
| Descriptive text | `"description": "string"` | `"entries": ["string", {type,name,entries}]` |
| Size | `"size": "Medium"` | `"size": ["M"]` |
| Alignment | `"alignment": "Neutral Good"` | `"alignment": ["N", "G"]` |
| Spell school | `"school": "Evocation"` | `"school": "EV"` |
| Ability scores | `"abilityScores": {strength: 10}` | `"str": 10, "dex": 14, ...` (flat) |
| Challenge rating | `"challengeRating": {cr,crNumeric,xp,proficiencyBonus}` | `"cr": "1/4"` |
| Skill values | `"skills": {"perception": 5}` | `"skill": {"perception": "+5"}` |
| Casting time | `"castingTime": "1 action"` | `"time": [{"number": 1, "unit": "action"}]` |
| Spell range | `"range": "60 feet"` | `"range": {"type":"point","distance":{"type":"feet","amount":60}}` |
| Duration | `"duration": "Instantaneous"` | `"duration": [{"type":"instant"}]` |

**Inline tags:** 5etools uses `{@damage 2d8}`, `{@dc 15}`, `{@condition prone}`, `{@item Javelin|PHB}` etc. inside `entries` strings. These are preserved in `fields` as-is. They are stripped to plain text when generating `canonicalText` for Qdrant embedding. This lets future consumers parse tags for structured filtering (e.g. "spells dealing d8s", "abilities with DC > 15").

**Extraction model:** Currently qwen3:8b. Producing valid `entries` arrays is harder than flat strings — the model may be swapped to qwen3:14b or qwen3:32b if output quality drops. Model is a config value, not a hard dependency.

---

## Phase 1 — Types with 5etools Equivalents (17 types)

### Spell
| Old field | New field |
|---|---|
| `school` string | `school` code (`"EV"`, `"C"`, `"A"`, `"T"`, `"D"`, `"N"`, `"I"`, `"EN"`) |
| `castingTime` string | `time: [{number, unit}]` |
| `range` string | `range: {type, distance: {type, amount}}` |
| `duration` string | `duration: [{type, ...}]` |
| `description` string | `entries[]` |
| `atHigherLevels` string | `entriesHigherLevel[]` |
| `damageTypes[]` | `damageInflict[]` |
| `classes[]` | `classes[]` (keep as string array, simplified from 5etools) |
| `ritual`, `concentration`, `components`, `level` | unchanged |

### Monster
| Old field | New field |
|---|---|
| `size` string | `size: ["M"]` code array |
| `type` string + `subtypes[]` | `type: {type: "humanoid", tags: [...]}` |
| `alignment` string | `alignment: ["N", "G"]` code array |
| `armorClass: {value, source}` | `ac: [12]` or `ac: [{ac:12, from:["natural armor"]}]` |
| `hitPoints: {average, dice}` | `hp: {average, formula}` |
| `abilityScores: {strength,...}` | flat `str, dex, con, int, wis, cha` |
| `skills: {perception: 5}` | `skill: {perception: "+5"}` |
| `challengeRating: {cr,...}` | `cr: "1/4"` |
| `senses: {darkvision: 60}` | `senses: ["darkvision 60 ft."]` + `passive: 15` |
| `traits[]` with `summary` | `trait[]` with `entries[]` |
| `actions[]` | `action[]` with `entries[]` |
| `bonusActions[]` | `bonus[]` with `entries[]` |
| `reactions[]` | `reaction[]` with `entries[]` |
| `legendaryActions: {perTurn, actions[]}` | `legendary[]` + `legendaryHeader[]` |
| `lairActions: {initiativeCount, actions[], regionalEffects[]}` | `lair[]` + `lairHeader[]` + `regionalEntries[]` |
| `spellcasting` block | `spellcasting[]` array (5etools format) |

### Race
| Old field | New field |
|---|---|
| `size` string | `size: ["M"]` code array |
| `speed` integer | `speed: {walk: 30}` object |
| `abilityBonuses[]` | `ability: [{dex:2, wis:1}]` |
| `traits[]` with `summary` | `entries[]` |
| `languages[]` | `languageProficiencies: [{common:true}]` |
| `subraces[]` | removed (subraces are separate entities) |

### Class / Subclass
Simplified 5etools shape:
- `hd: {number:1, faces:8}` — hit die
- `proficiency[]` — saving throw proficiency codes  
- `startingProficiencies: {armor[], weapons[], skills}` 
- `classFeatures[]` — array of `{classFeature, level}` references
- `entries[]` — flavour description
- Subclass adds: `shortName`, `subclassFeatures[]`

### Background
- `entries[]` for description
- `skillProficiencies[]`, `languageProficiencies[]`, `startingEquipment[]`
- `feats[]` for background feats (2024 rules)

### Feat
- `prerequisite[]` for requirements
- `entries[]` for description
- `ability[]` for ASI grants

### God (Deity)
| Old field | New field |
|---|---|
| `alignment` string | `alignment: ["N", "E"]` code array |
| `description` string | `entries[]` |
| `domains[]` | unchanged |
| `symbol`, `pantheon`, `plane` | unchanged |
| — | `province` string (5etools field, deity's portfolio) |
| — | `category` string (pantheon grouping) |

### Trap
| Old field | New field |
|---|---|
| `difficulty` string | `trapHazType` code (`"MECH"`, `"MAG"`, `"SMPL"`, `"CMPX"`) |
| `description` string | `entries[]` |
| `detectDc`, `disarmDc` | **kept** as explicit fields — useful for MCP filtering, 5etools buries these in prose |
| — | `rating: [{tier, threat}]` (5etools severity) |
| `variants[]` | unchanged |

### Condition / DiseasePoison
- `entries[]` for description
- `conditionInflict[]` for conditions that inflict other conditions

### Item / Weapon / Armor / MagicItem
All items unify around:
- `type` code (`"M"` melee weapon, `"R"` ranged, `"LA"` light armor, `"SCF"` spellcasting focus, etc.)
- `entries[]` for description
- `rarity` string (magic items)
- `reqAttune` string or boolean (magic items)
- Weapons: `dmg1`, `dmgType`, `weaponCategory`, `property[]`, `range`
- Armor: `ac` integer, `strength` requirement, `stealth` disadvantage flag
- `variants[]` unchanged (our addition for +1/+2/+3 tiers)

### VehicleMount
- `entries[]` for description
- `speed: {walk, fly, swim}` object
- `capacity` string

---

## Phase 2 — Custom Types (no 5etools equivalent, 5 types)

These adopt 5etools conventions (entries, source/page on outer wrapper) but fields are ours.

### Plane
```json
{
  "category": "Outer",
  "entries": ["The Nine Hells of Baator..."],
  "relatedPlanes": ["Acheron", "Gehenna"]
}
```

### Location
```json
{
  "category": "Dungeon",
  "setting": "Forgotten Realms",
  "entries": ["A vast underground complex..."]
}
```

### Faction
```json
{
  "headquarters": "Waterdeep",
  "goals": ["Protect the innocent", "Maintain order"],
  "entries": ["The Lords' Alliance is a coalition..."]
}
```

### Lore
```json
{
  "category": "Cosmology",
  "settingContext": "Forgotten Realms",
  "entries": ["The planes of existence are divided..."]
}
```

### Rule
Adopts 5etools `variantrule` shape:
```json
{
  "ruleType": "O",
  "entries": ["This section provides new action options..."]
}
```
`ruleType` codes: `"C"` core, `"O"` optional, `"V"` variant.
`category` field dropped — `ruleType` covers classification. `sourceTable` dropped — belongs in entries.

---

## Extraction Prompt Changes

`ExtractionPromptBuilder.BuildSystemPrompt` updated per entity type to include:

**Code lookup tables** (injected for relevant types):
- Size: `T=Tiny, S=Small, M=Medium, L=Large, H=Huge, G=Gargantuan`
- Alignment: `L=Lawful, C=Chaotic, G=Good, E=Evil, N=Neutral, U=Unaligned, A=Any`
- Spell school: `A=Abjuration, C=Conjuration, D=Divination, EN=Enchantment, EV=Evocation, I=Illusion, N=Necromancy, T=Transmutation`
- Rule type: `C=Core, O=Optional, V=Variant`

**entries format guidance:**
```
Produce entries as a JSON array. Plain paragraphs are strings.
Named subsections use: {"type":"entries","name":"Section Name","entries":["..."]}
Use inline tags: {@damage 2d8} for damage rolls, {@dc 15} for DCs,
{@condition prone} for conditions, {@item Javelin|PHB} for item references.
```

**Per-type example snippet** — a minimal valid output shown in the prompt for each entity type.

---

## Migration

| Artifact | Change |
|---|---|
| 22 `*Fields.schema.json` | Rewritten to 5etools format |
| `phb14.json` (3 sample entities) | Fields migrated to new format |
| `test-book.json` fixtures | Updated to match new schemas |
| `SchemaGenerationTests` | Updated fixture assertions |
| `SchemaRequiredFieldsTests` | Updated field names in test JSON |
| `ExtractionPromptBuilder.cs` | Code tables + entries guidance per type |
| `ExtractionPromptBuilderTests.cs` | Updated assertions for new prompt content |
| C# application code | **No changes** — fields are opaque `JsonElement` |

---

## Out of Scope (Plan 2)

- 5etools direct ingestion endpoint
- Source code mapping (`"PHB"` → `players-handbook` slug)
- `canonicalText` flattener (strips `{@tags}`, joins `entries` to plain text)
- Qdrant re-ingestion of existing data
