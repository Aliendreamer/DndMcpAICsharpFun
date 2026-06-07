# 5etools Schema Alignment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Rewrite all 22 canonical entity schemas to match 5etools JSON format so that 5etools data can be ingested without transformation and LLM extraction targets the same format.

**Architecture:** Pure data/config changes — JSON schema files, test fixtures, and `ExtractionPromptBuilder` system prompts. No C# model or application code changes (`fields` is stored as opaque `JsonElement` throughout). Each task: update fixture to new format → test fails (schema rejects new fields) → rewrite schema → test passes → commit.

**Tech Stack:** JSON Schema Draft 4, NJsonSchema (validation), xUnit + FluentAssertions, C# / .NET 10

---

## File Map

| File | Action |
| --- | --- |
| `Schemas/canonical/SpellFields.schema.json` | Rewrite |
| `Schemas/canonical/MonsterFields.schema.json` | Rewrite |
| `Schemas/canonical/RaceFields.schema.json` | Rewrite |
| `Schemas/canonical/SubraceFields.schema.json` | Rewrite |
| `Schemas/canonical/ClassFields.schema.json` | Rewrite |
| `Schemas/canonical/SubclassFields.schema.json` | Rewrite |
| `Schemas/canonical/BackgroundFields.schema.json` | Rewrite |
| `Schemas/canonical/FeatFields.schema.json` | Rewrite |
| `Schemas/canonical/WeaponFields.schema.json` | Rewrite |
| `Schemas/canonical/ArmorFields.schema.json` | Rewrite |
| `Schemas/canonical/ItemFields.schema.json` | Rewrite |
| `Schemas/canonical/MagicItemFields.schema.json` | Rewrite (keep variants) |
| `Schemas/canonical/GodFields.schema.json` | Rewrite (keep required) |
| `Schemas/canonical/TrapFields.schema.json` | Rewrite (keep variants + detectDc/disarmDc) |
| `Schemas/canonical/ConditionFields.schema.json` | Rewrite |
| `Schemas/canonical/DiseasePoisonFields.schema.json` | Rewrite |
| `Schemas/canonical/VehicleMountFields.schema.json` | Rewrite |
| `Schemas/canonical/PlaneFields.schema.json` | Rewrite (keep required) |
| `Schemas/canonical/LocationFields.schema.json` | Rewrite (keep required) |
| `Schemas/canonical/FactionFields.schema.json` | Rewrite (keep required) |
| `Schemas/canonical/LoreFields.schema.json` | Rewrite (keep required) |
| `Schemas/canonical/RuleFields.schema.json` | Rewrite (keep required) |
| `DndMcpAICsharpFun.Tests/Fixtures/canonical/test-book.json` | Migrate all 5 entities + add 17 new fixture entities (one per remaining type) |
| `DndMcpAICsharpFun.Tests/Entities/Schemas/SchemaGenerationTests.cs` | Add `[InlineData]` for all 22 types |
| `Features/Ingestion/EntityExtraction/ExtractionPromptBuilder.cs` | Add code lookup tables + entries format guidance |
| `DndMcpAICsharpFun.Tests/Entities/Extraction/ExtractionPromptBuilderTests.cs` | Add tests for new prompt content |
| `data/canonical/phb14.json` | Migrate 3 sample entities to new format |

---

### Task 1: Spell schema

**Files:**

- Modify: `Schemas/canonical/SpellFields.schema.json`
- Modify: `DndMcpAICsharpFun.Tests/Fixtures/canonical/test-book.json`
- Modify: `DndMcpAICsharpFun.Tests/Entities/Schemas/SchemaGenerationTests.cs`

- [x] **Step 1: Update the spell fixture to 5etools format (test will fail)**

In `test-book.json`, replace the `fields` of `test-book.spell.fireball` with:

```json
{
  "level": 3,
  "school": "EV",
  "time": [{ "number": 1, "unit": "action" }],
  "range": { "type": "point", "distance": { "type": "feet", "amount": 150 } },
  "components": { "v": true, "s": true, "m": true, "material": "a tiny ball of bat guano and sulfur" },
  "duration": [{ "type": "instant" }],
  "ritual": false,
  "concentration": false,
  "entries": [
    "A bright streak flashes from your pointing finger to a point you choose within range and then blossoms with a low roar into an explosion of flame. Each creature in a 20-foot-radius sphere centered on that point must make a Dexterity saving throw. A target takes {@damage 8d6} fire damage on a failed save, or half as much damage on a successful one.",
    "The fire spreads around corners. It ignites flammable objects in the area that aren't being worn or carried."
  ],
  "entriesHigherLevel": [
    "When you cast this spell using a spell slot of 4th level or higher, the damage increases by {@damage 1d6} for each slot level above 3rd."
  ],
  "damageInflict": ["fire"],
  "classes": ["Sorcerer", "Wizard"]
}
```

- [x] **Step 2: Run test to verify it fails**

```bash
dotnet test DndMcpAICsharpFun.Tests/ --filter "Generated_schema_validates_fixture_entity" -q
```
Expected: FAIL — `time` and `range` are additional properties not in the current SpellFields schema.

- [x] **Step 3: Rewrite `SpellFields.schema.json`**

```json
{
  "$schema": "http://json-schema.org/draft-04/schema#",
  "title": "SpellFields",
  "type": "object",
  "additionalProperties": false,
  "properties": {
    "level": { "type": "integer", "format": "int32" },
    "school": { "type": "string" },
    "time": {
      "type": ["array", "null"],
      "items": {
        "type": "object",
        "additionalProperties": false,
        "properties": {
          "number": { "type": "integer", "format": "int32" },
          "unit": { "type": "string" },
          "condition": { "type": ["null", "string"] }
        }
      }
    },
    "range": {
      "oneOf": [
        { "type": "null" },
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "type": { "type": "string" },
            "distance": {
              "type": ["object", "null"],
              "properties": {
                "type": { "type": "string" },
                "amount": { "type": ["integer", "null"], "format": "int32" }
              }
            }
          }
        }
      ]
    },
    "components": {
      "type": ["object", "null"],
      "additionalProperties": false,
      "properties": {
        "v": { "type": "boolean" },
        "s": { "type": "boolean" },
        "m": { "type": ["boolean", "object"] },
        "material": { "type": ["null", "string"] }
      }
    },
    "duration": {
      "type": ["array", "null"],
      "items": {
        "type": "object",
        "properties": {
          "type": { "type": "string" },
          "duration": { "type": ["object", "null"] },
          "concentration": { "type": "boolean" },
          "ends": { "type": ["array", "null"] }
        }
      }
    },
    "ritual": { "type": "boolean" },
    "concentration": { "type": "boolean" },
    "entries": {
      "type": ["array", "null"],
      "items": { "type": ["string", "object"] }
    },
    "entriesHigherLevel": {
      "type": ["array", "null"],
      "items": { "type": ["string", "object"] }
    },
    "damageInflict": {
      "type": ["array", "null"],
      "items": { "type": "string" }
    },
    "savingThrow": {
      "type": ["array", "null"],
      "items": { "type": "string" }
    },
    "classes": {
      "type": ["array", "null"],
      "items": { "type": "string" }
    },
    "miscTags": {
      "type": ["array", "null"],
      "items": { "type": "string" }
    },
    "areaTags": {
      "type": ["array", "null"],
      "items": { "type": "string" }
    },
    "scalingLevelDice": { "type": ["object", "null"] },
    "conditionInflict": {
      "type": ["array", "null"],
      "items": { "type": "string" }
    }
  }
}
```

- [x] **Step 4: Run test to verify it passes**

```bash
dotnet test DndMcpAICsharpFun.Tests/ --filter "Generated_schema_validates_fixture_entity" -q
```
Expected: PASS for `test-book.spell.fireball`.

- [x] **Step 5: Run full suite**

```bash
dotnet test DndMcpAICsharpFun.Tests/ -q
```
Expected: all passing.

- [x] **Step 6: Commit**

```bash
git add Schemas/canonical/SpellFields.schema.json DndMcpAICsharpFun.Tests/Fixtures/canonical/test-book.json
git commit -m "feat(schemas): align SpellFields to 5etools format"
```

---

### Task 2: Monster schema

**Files:**

- Modify: `Schemas/canonical/MonsterFields.schema.json`
- Modify: `DndMcpAICsharpFun.Tests/Fixtures/canonical/test-book.json`

- [x] **Step 1: Update the monster fixture to 5etools format**

In `test-book.json`, replace `test-book.monster.bullywug` fields with:

```json
{
  "size": ["M"],
  "type": { "type": "humanoid", "tags": ["bullywug"] },
  "alignment": ["N", "E"],
  "ac": [15],
  "hp": { "average": 11, "formula": "2d8+2" },
  "speed": { "walk": 20, "swim": 40 },
  "str": 12, "dex": 12, "con": 13, "int": 7, "wis": 10, "cha": 7,
  "skill": { "stealth": "+3", "perception": "+2" },
  "passive": 12,
  "languages": ["Bullywug"],
  "cr": "1/4",
  "trait": [
    { "name": "Amphibious", "entries": ["The bullywug can breathe air and water."] },
    { "name": "Speak with Frogs and Toads", "entries": ["The bullywug can communicate simple concepts to frogs and toads when it speaks in Bullywug."] }
  ],
  "action": [
    { "name": "Multiattack", "entries": ["The bullywug makes two melee attacks: one with its bite and one with its spear."] },
    { "name": "Bite", "entries": ["{@atk mw} {@hit 3} to hit, reach 5 ft., one target. {@h}{@damage 1d4+1} bludgeoning damage."] },
    { "name": "Spear", "entries": ["{@atk mw,rw} {@hit 3} to hit, reach 5 ft. or range 20/60 ft., one target. {@h}{@damage 1d6+1} piercing damage."] }
  ]
}
```

- [x] **Step 2: Run test to verify it fails**

```bash
dotnet test DndMcpAICsharpFun.Tests/ --filter "Generated_schema_validates_fixture_entity" -q
```
Expected: FAIL — `size` array, `type` object, `ac` array, flat ability scores all violate current MonsterFields schema.

- [x] **Step 3: Rewrite `MonsterFields.schema.json`**

```json
{
  "$schema": "http://json-schema.org/draft-04/schema#",
  "title": "MonsterFields",
  "type": "object",
  "additionalProperties": false,
  "required": ["cr", "size", "type"],
  "properties": {
    "size": { "type": "array", "items": { "type": "string" } },
    "type": {
      "oneOf": [
        { "type": "string" },
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "type": { "type": "string" },
            "tags": { "type": ["array", "null"], "items": { "type": "string" } }
          }
        }
      ]
    },
    "alignment": { "type": ["array", "null"], "items": { "type": "string" } },
    "ac": {
      "type": ["array", "null"],
      "items": {
        "oneOf": [
          { "type": "integer", "format": "int32" },
          {
            "type": "object",
            "properties": {
              "ac": { "type": "integer", "format": "int32" },
              "from": { "type": ["array", "null"], "items": { "type": "string" } },
              "condition": { "type": ["null", "string"] }
            }
          }
        ]
      }
    },
    "hp": {
      "type": ["object", "null"],
      "additionalProperties": false,
      "properties": {
        "average": { "type": "integer", "format": "int32" },
        "formula": { "type": "string" },
        "special": { "type": ["null", "string"] }
      }
    },
    "speed": { "type": ["object", "null"] },
    "str": { "type": ["integer", "null"], "format": "int32" },
    "dex": { "type": ["integer", "null"], "format": "int32" },
    "con": { "type": ["integer", "null"], "format": "int32" },
    "int": { "type": ["integer", "null"], "format": "int32" },
    "wis": { "type": ["integer", "null"], "format": "int32" },
    "cha": { "type": ["integer", "null"], "format": "int32" },
    "save": { "type": ["object", "null"] },
    "skill": { "type": ["object", "null"] },
    "resist": { "type": ["array", "null"], "items": { "type": ["string", "object"] } },
    "immune": { "type": ["array", "null"], "items": { "type": ["string", "object"] } },
    "vulnerable": { "type": ["array", "null"], "items": { "type": ["string", "object"] } },
    "conditionImmune": { "type": ["array", "null"], "items": { "type": ["string", "object"] } },
    "senses": { "type": ["array", "null"], "items": { "type": "string" } },
    "passive": { "type": ["integer", "null"], "format": "int32" },
    "languages": { "type": ["array", "null"], "items": { "type": "string" } },
    "cr": {
      "oneOf": [
        { "type": "string" },
        {
          "type": "object",
          "properties": {
            "cr": { "type": "string" },
            "lair": { "type": ["null", "string"] },
            "coven": { "type": ["null", "string"] }
          }
        }
      ]
    },
    "trait": {
      "type": ["array", "null"],
      "items": {
        "type": "object",
        "properties": {
          "name": { "type": "string" },
          "entries": { "type": "array", "items": { "type": ["string", "object"] } }
        }
      }
    },
    "action": {
      "type": ["array", "null"],
      "items": {
        "type": "object",
        "properties": {
          "name": { "type": "string" },
          "entries": { "type": "array", "items": { "type": ["string", "object"] } }
        }
      }
    },
    "bonus": {
      "type": ["array", "null"],
      "items": {
        "type": "object",
        "properties": {
          "name": { "type": "string" },
          "entries": { "type": "array", "items": { "type": ["string", "object"] } }
        }
      }
    },
    "reaction": {
      "type": ["array", "null"],
      "items": {
        "type": "object",
        "properties": {
          "name": { "type": "string" },
          "entries": { "type": "array", "items": { "type": ["string", "object"] } }
        }
      }
    },
    "legendary": {
      "type": ["array", "null"],
      "items": {
        "type": "object",
        "properties": {
          "name": { "type": ["null", "string"] },
          "entries": { "type": "array", "items": { "type": ["string", "object"] } }
        }
      }
    },
    "legendaryHeader": { "type": ["array", "null"], "items": { "type": "string" } },
    "legendaryGroup": { "type": ["object", "null"] },
    "lair": { "type": ["array", "null"], "items": { "type": ["string", "object"] } },
    "lairHeader": { "type": ["array", "null"], "items": { "type": "string" } },
    "regional": { "type": ["array", "null"], "items": { "type": ["string", "object"] } },
    "spellcasting": { "type": ["array", "null"], "items": { "type": "object" } },
    "environment": { "type": ["array", "null"], "items": { "type": "string" } },
    "variant": { "type": ["array", "null"], "items": { "type": "object" } },
    "variantForms": { "type": ["array", "null"], "items": { "type": "string" } },
    "miscTags": { "type": ["array", "null"], "items": { "type": "string" } },
    "damageTags": { "type": ["array", "null"], "items": { "type": "string" } },
    "senseTags": { "type": ["array", "null"], "items": { "type": "string" } },
    "languageTags": { "type": ["array", "null"], "items": { "type": "string" } },
    "actionTags": { "type": ["array", "null"], "items": { "type": "string" } }
  }
}
```

- [x] **Step 4: Run tests to verify they pass**

```bash
dotnet test DndMcpAICsharpFun.Tests/ -q
```
Expected: all passing.

- [x] **Step 5: Commit**

```bash
git add Schemas/canonical/MonsterFields.schema.json DndMcpAICsharpFun.Tests/Fixtures/canonical/test-book.json
git commit -m "feat(schemas): align MonsterFields to 5etools format"
```

---

### Task 3: Race + Subrace schemas

**Files:**

- Modify: `Schemas/canonical/RaceFields.schema.json`
- Modify: `Schemas/canonical/SubraceFields.schema.json`
- Modify: `DndMcpAICsharpFun.Tests/Fixtures/canonical/test-book.json`
- Modify: `DndMcpAICsharpFun.Tests/Entities/Schemas/SchemaGenerationTests.cs`

- [x] **Step 1: Add Race and Subrace fixtures + InlineData**

Add to `SchemaGenerationTests`:
```csharp
[InlineData("test-book.race.human", "RaceFields")]
[InlineData("test-book.subrace.high-elf", "SubraceFields")]
```

Add to `test-book.json` entities array:

```json
{
  "id": "test-book.race.human",
  "type": "Race",
  "name": "Human",
  "sourceBook": "Test Book",
  "edition": "Edition2014",
  "page": 29,
  "firstAppearedIn": { "book": "Test Book", "edition": "Edition2014" },
  "revisedIn": [],
  "settingTags": [],
  "canonicalText": "Humans are the most adaptable and ambitious people among the common races.",
  "fields": {
    "size": ["M"],
    "speed": { "walk": 30 },
    "ability": [{ "str": 1, "dex": 1, "con": 1, "int": 1, "wis": 1, "cha": 1 }],
    "languageProficiencies": [{ "common": true, "anyStandard": 1 }],
    "entries": ["Humans are the most adaptable and ambitious people among the common races."]
  }
},
{
  "id": "test-book.subrace.high-elf",
  "type": "Subrace",
  "name": "High Elf",
  "sourceBook": "Test Book",
  "edition": "Edition2014",
  "page": 23,
  "firstAppearedIn": { "book": "Test Book", "edition": "Edition2014" },
  "revisedIn": [],
  "settingTags": [],
  "canonicalText": "As a high elf, you have a keen mind and a mastery of at least the basics of magic.",
  "fields": {
    "raceName": "Elf",
    "raceSource": "PHB",
    "ability": [{ "int": 1 }],
    "entries": ["As a high elf, you have a keen mind and a mastery of at least the basics of magic."]
  }
}
```

- [x] **Step 2: Run tests to verify they fail**

```bash
dotnet test DndMcpAICsharpFun.Tests/ --filter "Generated_schema_validates_fixture_entity" -q
```
Expected: FAIL for `test-book.race.human` and `test-book.subrace.high-elf` — `size` array and `ability` array not in current schemas.

- [x] **Step 3: Rewrite `RaceFields.schema.json`**

```json
{
  "$schema": "http://json-schema.org/draft-04/schema#",
  "title": "RaceFields",
  "type": "object",
  "additionalProperties": false,
  "properties": {
    "size": { "type": ["array", "null"], "items": { "type": "string" } },
    "speed": { "type": ["object", "null"] },
    "ability": {
      "type": ["array", "null"],
      "items": { "type": "object" }
    },
    "languageProficiencies": {
      "type": ["array", "null"],
      "items": { "type": "object" }
    },
    "skillProficiencies": {
      "type": ["array", "null"],
      "items": { "type": "object" }
    },
    "toolProficiencies": {
      "type": ["array", "null"],
      "items": { "type": "object" }
    },
    "weaponProficiencies": {
      "type": ["array", "null"],
      "items": { "type": "object" }
    },
    "armorProficiencies": {
      "type": ["array", "null"],
      "items": { "type": "object" }
    },
    "darkvision": { "type": ["integer", "null"], "format": "int32" },
    "traitTags": { "type": ["array", "null"], "items": { "type": "string" } },
    "additionalSpells": { "type": ["array", "null"], "items": { "type": "object" } },
    "resist": { "type": ["array", "null"], "items": { "type": ["string", "object"] } },
    "immune": { "type": ["array", "null"], "items": { "type": ["string", "object"] } },
    "entries": { "type": ["array", "null"], "items": { "type": ["string", "object"] } },
    "lineage": { "type": ["null", "string"] }
  }
}
```

- [x] **Step 4: Rewrite `SubraceFields.schema.json`**

```json
{
  "$schema": "http://json-schema.org/draft-04/schema#",
  "title": "SubraceFields",
  "type": "object",
  "additionalProperties": false,
  "properties": {
    "raceName": { "type": "string" },
    "raceSource": { "type": ["null", "string"] },
    "ability": { "type": ["array", "null"], "items": { "type": "object" } },
    "speed": { "type": ["object", "null"] },
    "darkvision": { "type": ["integer", "null"], "format": "int32" },
    "skillProficiencies": { "type": ["array", "null"], "items": { "type": "object" } },
    "languageProficiencies": { "type": ["array", "null"], "items": { "type": "object" } },
    "weaponProficiencies": { "type": ["array", "null"], "items": { "type": "object" } },
    "additionalSpells": { "type": ["array", "null"], "items": { "type": "object" } },
    "resist": { "type": ["array", "null"], "items": { "type": ["string", "object"] } },
    "traitTags": { "type": ["array", "null"], "items": { "type": "string" } },
    "entries": { "type": ["array", "null"], "items": { "type": ["string", "object"] } }
  }
}
```

- [x] **Step 5: Run tests to verify they pass**

```bash
dotnet test DndMcpAICsharpFun.Tests/ -q
```
Expected: all passing.

- [x] **Step 6: Commit**

```bash
git add Schemas/canonical/RaceFields.schema.json Schemas/canonical/SubraceFields.schema.json \
  DndMcpAICsharpFun.Tests/Fixtures/canonical/test-book.json \
  DndMcpAICsharpFun.Tests/Entities/Schemas/SchemaGenerationTests.cs
git commit -m "feat(schemas): align RaceFields and SubraceFields to 5etools format"
```

---

### Task 4: Class + Subclass schemas

**Files:**

- Modify: `Schemas/canonical/ClassFields.schema.json`
- Modify: `Schemas/canonical/SubclassFields.schema.json`
- Modify: `DndMcpAICsharpFun.Tests/Fixtures/canonical/test-book.json`
- Modify: `DndMcpAICsharpFun.Tests/Entities/Schemas/SchemaGenerationTests.cs`

- [x] **Step 1: Update Class fixture + add Subclass fixture and InlineData**

Update `test-book.class.fighter` fields in `test-book.json`:

```json
{
  "hd": { "number": 1, "faces": 10 },
  "proficiency": ["str", "con"],
  "startingProficiencies": {
    "armor": ["light", "medium", "heavy", "shields"],
    "weapons": ["simple", "martial"],
    "skills": { "choose": { "from": ["Acrobatics", "Athletics", "History", "Insight", "Intimidation", "Perception", "Survival"], "count": 2 } }
  },
  "classFeatures": [
    { "classFeature": "Fighting Style|Fighter||1", "level": 1 },
    { "classFeature": "Second Wind|Fighter||1", "level": 1 },
    { "classFeature": "Action Surge|Fighter||2", "level": 2 }
  ],
  "entries": ["A master of martial combat, skilled with a variety of weapons and armor."]
}
```

Add `[InlineData("test-book.subclass.battle-master", "SubclassFields")]` to `SchemaGenerationTests`.

Add to `test-book.json` entities array:

```json
{
  "id": "test-book.subclass.battle-master",
  "type": "Subclass",
  "name": "Battle Master",
  "sourceBook": "Test Book",
  "edition": "Edition2014",
  "page": 73,
  "firstAppearedIn": { "book": "Test Book", "edition": "Edition2014" },
  "revisedIn": [],
  "settingTags": [],
  "canonicalText": "Those who emulate the archetypal Battle Master employ martial techniques passed down through generations.",
  "fields": {
    "className": "Fighter",
    "classSource": "PHB",
    "shortName": "Battle Master",
    "subclassFeatures": [
      { "subclassFeature": "Combat Superiority|Fighter|PHB|Battle Master|PHB|3", "level": 3 },
      { "subclassFeature": "Know Your Enemy|Fighter|PHB|Battle Master|PHB|7", "level": 7 }
    ],
    "entries": ["Those who emulate the archetypal Battle Master employ martial techniques passed down through generations."]
  }
}
```

- [x] **Step 2: Run tests to verify they fail**

```bash
dotnet test DndMcpAICsharpFun.Tests/ --filter "Generated_schema_validates_fixture_entity" -q
```
Expected: FAIL for class and subclass fixtures — `hd`, `classFeatures`, `subclassFeatures` not in current schemas.

- [x] **Step 3: Rewrite `ClassFields.schema.json`**

```json
{
  "$schema": "http://json-schema.org/draft-04/schema#",
  "title": "ClassFields",
  "type": "object",
  "additionalProperties": false,
  "properties": {
    "hd": {
      "type": ["object", "null"],
      "additionalProperties": false,
      "properties": {
        "number": { "type": "integer", "format": "int32" },
        "faces": { "type": "integer", "format": "int32" }
      }
    },
    "proficiency": { "type": ["array", "null"], "items": { "type": "string" } },
    "startingProficiencies": { "type": ["object", "null"] },
    "startingEquipment": { "type": ["array", "null"], "items": { "type": "object" } },
    "classFeatures": {
      "type": ["array", "null"],
      "items": {
        "type": "object",
        "properties": {
          "classFeature": { "type": "string" },
          "level": { "type": "integer", "format": "int32" }
        }
      }
    },
    "subclassTitle": { "type": ["null", "string"] },
    "spellcastingAbility": { "type": ["null", "string"] },
    "casterProgression": { "type": ["null", "string"] },
    "preparedSpells": { "type": ["null", "string"] },
    "entries": { "type": ["array", "null"], "items": { "type": ["string", "object"] } },
    "multiclassing": { "type": ["object", "null"] }
  }
}
```

- [x] **Step 4: Rewrite `SubclassFields.schema.json`**

```json
{
  "$schema": "http://json-schema.org/draft-04/schema#",
  "title": "SubclassFields",
  "type": "object",
  "additionalProperties": false,
  "properties": {
    "className": { "type": "string" },
    "classSource": { "type": ["null", "string"] },
    "shortName": { "type": ["null", "string"] },
    "subclassFeatures": {
      "type": ["array", "null"],
      "items": {
        "type": "object",
        "properties": {
          "subclassFeature": { "type": "string" },
          "level": { "type": "integer", "format": "int32" }
        }
      }
    },
    "spellcastingAbility": { "type": ["null", "string"] },
    "casterProgression": { "type": ["null", "string"] },
    "additionalSpells": { "type": ["array", "null"], "items": { "type": "object" } },
    "entries": { "type": ["array", "null"], "items": { "type": ["string", "object"] } }
  }
}
```

- [x] **Step 5: Run tests to verify they pass**

```bash
dotnet test DndMcpAICsharpFun.Tests/ -q
```
Expected: all passing.

- [x] **Step 6: Commit**

```bash
git add Schemas/canonical/ClassFields.schema.json Schemas/canonical/SubclassFields.schema.json \
  DndMcpAICsharpFun.Tests/Fixtures/canonical/test-book.json \
  DndMcpAICsharpFun.Tests/Entities/Schemas/SchemaGenerationTests.cs
git commit -m "feat(schemas): align ClassFields and SubclassFields to 5etools format"
```

---

### Task 5: Background + Feat schemas

**Files:**

- Modify: `Schemas/canonical/BackgroundFields.schema.json`
- Modify: `Schemas/canonical/FeatFields.schema.json`
- Modify: `DndMcpAICsharpFun.Tests/Fixtures/canonical/test-book.json`
- Modify: `DndMcpAICsharpFun.Tests/Entities/Schemas/SchemaGenerationTests.cs`

- [x] **Step 1: Add Background and Feat fixtures + InlineData**

Add to `SchemaGenerationTests`:
```csharp
[InlineData("test-book.background.acolyte", "BackgroundFields")]
[InlineData("test-book.feat.alert", "FeatFields")]
```

Add to `test-book.json` entities:

```json
{
  "id": "test-book.background.acolyte",
  "type": "Background",
  "name": "Acolyte",
  "sourceBook": "Test Book",
  "edition": "Edition2014",
  "page": 127,
  "firstAppearedIn": { "book": "Test Book", "edition": "Edition2014" },
  "revisedIn": [],
  "settingTags": [],
  "canonicalText": "You have spent your life in the service of a temple to a specific god or pantheon of gods.",
  "fields": {
    "skillProficiencies": [{ "insight": true, "religion": true }],
    "languageProficiencies": [{ "anyStandard": 2 }],
    "startingEquipment": [
      { "item": "holy symbol" },
      { "item": "prayer book" },
      { "value": 15000 }
    ],
    "entries": [
      "You have spent your life in the service of a temple to a specific god or pantheon of gods.",
      {
        "type": "entries",
        "name": "Feature: Shelter of the Faithful",
        "entries": ["As an acolyte, you command the respect of those who share your faith."]
      }
    ]
  }
},
{
  "id": "test-book.feat.alert",
  "type": "Feat",
  "name": "Alert",
  "sourceBook": "Test Book",
  "edition": "Edition2014",
  "page": 165,
  "firstAppearedIn": { "book": "Test Book", "edition": "Edition2014" },
  "revisedIn": [],
  "settingTags": [],
  "canonicalText": "Always on the lookout for danger, you gain the following benefits.",
  "fields": {
    "entries": [
      "Always on the lookout for danger, you gain the following benefits.",
      { "type": "list", "items": [
        "You gain a +5 bonus to initiative.",
        "You can't be surprised while you are conscious.",
        "Other creatures don't gain advantage on attack rolls against you as a result of being unseen by you."
      ]}
    ]
  }
}
```

- [x] **Step 2: Run tests to verify they fail**

```bash
dotnet test DndMcpAICsharpFun.Tests/ --filter "Generated_schema_validates_fixture_entity" -q
```
Expected: FAIL — `skillProficiencies` as objects and `startingEquipment` as objects not in current schemas.

- [x] **Step 3: Rewrite `BackgroundFields.schema.json`**

```json
{
  "$schema": "http://json-schema.org/draft-04/schema#",
  "title": "BackgroundFields",
  "type": "object",
  "additionalProperties": false,
  "properties": {
    "skillProficiencies": { "type": ["array", "null"], "items": { "type": "object" } },
    "languageProficiencies": { "type": ["array", "null"], "items": { "type": "object" } },
    "toolProficiencies": { "type": ["array", "null"], "items": { "type": "object" } },
    "startingEquipment": { "type": ["array", "null"], "items": { "type": "object" } },
    "feats": { "type": ["array", "null"], "items": { "type": "object" } },
    "entries": { "type": ["array", "null"], "items": { "type": ["string", "object"] } }
  }
}
```

- [x] **Step 4: Rewrite `FeatFields.schema.json`**

```json
{
  "$schema": "http://json-schema.org/draft-04/schema#",
  "title": "FeatFields",
  "type": "object",
  "additionalProperties": false,
  "properties": {
    "prerequisite": { "type": ["array", "null"], "items": { "type": "object" } },
    "ability": { "type": ["array", "null"], "items": { "type": "object" } },
    "skillProficiencies": { "type": ["array", "null"], "items": { "type": "object" } },
    "languageProficiencies": { "type": ["array", "null"], "items": { "type": "object" } },
    "toolProficiencies": { "type": ["array", "null"], "items": { "type": "object" } },
    "additionalSpells": { "type": ["array", "null"], "items": { "type": "object" } },
    "resist": { "type": ["array", "null"], "items": { "type": ["string", "object"] } },
    "entries": { "type": ["array", "null"], "items": { "type": ["string", "object"] } }
  }
}
```

- [x] **Step 5: Run tests to verify they pass**

```bash
dotnet test DndMcpAICsharpFun.Tests/ -q
```
Expected: all passing.

- [x] **Step 6: Commit**

```bash
git add Schemas/canonical/BackgroundFields.schema.json Schemas/canonical/FeatFields.schema.json \
  DndMcpAICsharpFun.Tests/Fixtures/canonical/test-book.json \
  DndMcpAICsharpFun.Tests/Entities/Schemas/SchemaGenerationTests.cs
git commit -m "feat(schemas): align BackgroundFields and FeatFields to 5etools format"
```

---

### Task 6: Weapon + Armor schemas

**Files:**

- Modify: `Schemas/canonical/WeaponFields.schema.json`
- Modify: `Schemas/canonical/ArmorFields.schema.json`
- Modify: `DndMcpAICsharpFun.Tests/Fixtures/canonical/test-book.json`
- Modify: `DndMcpAICsharpFun.Tests/Entities/Schemas/SchemaGenerationTests.cs`

- [x] **Step 1: Add Weapon and Armor fixtures + InlineData**

Add to `SchemaGenerationTests`:
```csharp
[InlineData("test-book.weapon.longsword", "WeaponFields")]
[InlineData("test-book.armor.chain-mail", "ArmorFields")]
```

Add to `test-book.json`:

```json
{
  "id": "test-book.weapon.longsword",
  "type": "Weapon",
  "name": "Longsword",
  "sourceBook": "Test Book",
  "edition": "Edition2014",
  "page": 149,
  "firstAppearedIn": { "book": "Test Book", "edition": "Edition2014" },
  "revisedIn": [],
  "settingTags": [],
  "canonicalText": "A longsword is a martial melee weapon dealing 1d8 slashing damage.",
  "fields": {
    "type": "M",
    "weaponCategory": "martial",
    "dmg1": "1d8",
    "dmgType": "S",
    "dmg2": "1d10",
    "property": ["V"],
    "value": 1500,
    "weight": 3,
    "entries": ["Proficiency with a longsword allows you to add your proficiency bonus to the attack roll for any attack you make with it."]
  }
},
{
  "id": "test-book.armor.chain-mail",
  "type": "Armor",
  "name": "Chain Mail",
  "sourceBook": "Test Book",
  "edition": "Edition2014",
  "page": 145,
  "firstAppearedIn": { "book": "Test Book", "edition": "Edition2014" },
  "revisedIn": [],
  "settingTags": [],
  "canonicalText": "Chain mail is heavy armor providing AC 16.",
  "fields": {
    "type": "HA",
    "ac": 16,
    "strength": 13,
    "stealth": true,
    "value": 7500,
    "weight": 55,
    "entries": ["Made of interlocking metal rings, chain mail includes a layer of quilted fabric worn underneath to prevent chafing."]
  }
}
```

- [x] **Step 2: Run tests to verify they fail**

```bash
dotnet test DndMcpAICsharpFun.Tests/ --filter "Generated_schema_validates_fixture_entity" -q
```
Expected: FAIL — `weaponCategory`, `dmg1`, `dmgType` not in WeaponFields; `type` code and `ac` integer not in ArmorFields.

- [x] **Step 3: Rewrite `WeaponFields.schema.json`**

```json
{
  "$schema": "http://json-schema.org/draft-04/schema#",
  "title": "WeaponFields",
  "type": "object",
  "additionalProperties": false,
  "properties": {
    "type": { "type": ["null", "string"] },
    "weaponCategory": { "type": ["null", "string"] },
    "dmg1": { "type": ["null", "string"] },
    "dmg2": { "type": ["null", "string"] },
    "dmgType": { "type": ["null", "string"] },
    "property": { "type": ["array", "null"], "items": { "type": "string" } },
    "range": { "type": ["null", "string"] },
    "reload": { "type": ["integer", "null"], "format": "int32" },
    "value": { "type": ["integer", "null"], "format": "int32" },
    "weight": { "type": ["number", "null"], "format": "double" },
    "age": { "type": ["null", "string"] },
    "firearm": { "type": ["boolean", "null"] },
    "ammoType": { "type": ["null", "string"] },
    "entries": { "type": ["array", "null"], "items": { "type": ["string", "object"] } }
  }
}
```

- [x] **Step 4: Rewrite `ArmorFields.schema.json`**

```json
{
  "$schema": "http://json-schema.org/draft-04/schema#",
  "title": "ArmorFields",
  "type": "object",
  "additionalProperties": false,
  "properties": {
    "type": { "type": ["null", "string"] },
    "ac": { "type": ["integer", "null"], "format": "int32" },
    "strength": { "type": ["integer", "null"], "format": "int32" },
    "stealth": { "type": ["boolean", "null"] },
    "value": { "type": ["integer", "null"], "format": "int32" },
    "weight": { "type": ["number", "null"], "format": "double" },
    "entries": { "type": ["array", "null"], "items": { "type": ["string", "object"] } }
  }
}
```

- [x] **Step 5: Run tests to verify they pass**

```bash
dotnet test DndMcpAICsharpFun.Tests/ -q
```
Expected: all passing.

- [x] **Step 6: Commit**

```bash
git add Schemas/canonical/WeaponFields.schema.json Schemas/canonical/ArmorFields.schema.json \
  DndMcpAICsharpFun.Tests/Fixtures/canonical/test-book.json \
  DndMcpAICsharpFun.Tests/Entities/Schemas/SchemaGenerationTests.cs
git commit -m "feat(schemas): align WeaponFields and ArmorFields to 5etools format"
```

---

### Task 7: Item + MagicItem schemas

**Files:**

- Modify: `Schemas/canonical/ItemFields.schema.json`
- Modify: `Schemas/canonical/MagicItemFields.schema.json`
- Modify: `DndMcpAICsharpFun.Tests/Fixtures/canonical/test-book.json`
- Modify: `DndMcpAICsharpFun.Tests/Entities/Schemas/SchemaGenerationTests.cs`

- [x] **Step 1: Add Item and MagicItem fixtures + InlineData**

Add to `SchemaGenerationTests`:
```csharp
[InlineData("test-book.item.backpack", "ItemFields")]
[InlineData("test-book.magic-item.cloak-of-protection", "MagicItemFields")]
```

Add to `test-book.json`:

```json
{
  "id": "test-book.item.backpack",
  "type": "Item",
  "name": "Backpack",
  "sourceBook": "Test Book",
  "edition": "Edition2014",
  "page": 153,
  "firstAppearedIn": { "book": "Test Book", "edition": "Edition2014" },
  "revisedIn": [],
  "settingTags": [],
  "canonicalText": "A backpack can hold one cubic foot or 30 pounds of gear.",
  "fields": {
    "type": "G",
    "value": 200,
    "weight": 5,
    "entries": ["A backpack can hold one cubic foot or 30 pounds of gear."]
  }
},
{
  "id": "test-book.magic-item.cloak-of-protection",
  "type": "MagicItem",
  "name": "Cloak of Protection",
  "sourceBook": "Test Book",
  "edition": "Edition2014",
  "page": 159,
  "firstAppearedIn": { "book": "Test Book", "edition": "Edition2014" },
  "revisedIn": [],
  "settingTags": [],
  "canonicalText": "You gain a +1 bonus to AC and saving throws while you wear this cloak.",
  "fields": {
    "type": "LA",
    "rarity": "uncommon",
    "reqAttune": true,
    "wondrous": true,
    "entries": ["You gain a +1 bonus to AC and saving throws while you wear this cloak."],
    "bonusAc": "+1",
    "bonusSavingThrow": "+1"
  }
}
```

- [x] **Step 2: Run tests to verify they fail**

```bash
dotnet test DndMcpAICsharpFun.Tests/ --filter "Generated_schema_validates_fixture_entity" -q
```
Expected: FAIL — `type` code and `entries` not in current ItemFields; `wondrous` and `bonusAc` not in current MagicItemFields.

- [x] **Step 3: Rewrite `ItemFields.schema.json`**

```json
{
  "$schema": "http://json-schema.org/draft-04/schema#",
  "title": "ItemFields",
  "type": "object",
  "additionalProperties": false,
  "properties": {
    "type": { "type": ["null", "string"] },
    "value": { "type": ["integer", "null"], "format": "int32" },
    "weight": { "type": ["number", "null"], "format": "double" },
    "entries": { "type": ["array", "null"], "items": { "type": ["string", "object"] } }
  }
}
```

- [x] **Step 4: Rewrite `MagicItemFields.schema.json`**

```json
{
  "$schema": "http://json-schema.org/draft-04/schema#",
  "title": "MagicItemFields",
  "type": "object",
  "additionalProperties": false,
  "properties": {
    "type": { "type": ["null", "string"] },
    "rarity": { "type": ["null", "string"] },
    "reqAttune": { "type": ["boolean", "null", "string"] },
    "reqAttuneTags": { "type": ["array", "null"], "items": { "type": "object" } },
    "wondrous": { "type": ["boolean", "null"] },
    "entries": { "type": ["array", "null"], "items": { "type": ["string", "object"] } },
    "bonusAc": { "type": ["null", "string"] },
    "bonusWeapon": { "type": ["null", "string"] },
    "bonusSpellAttack": { "type": ["null", "string"] },
    "bonusSpellSaveDc": { "type": ["null", "string"] },
    "bonusSavingThrow": { "type": ["null", "string"] },
    "focus": { "type": ["array", "null"], "items": { "type": "string" } },
    "attachedSpells": { "type": ["array", "null"], "items": { "type": "string" } },
    "charges": { "type": ["integer", "null"], "format": "int32" },
    "recharge": { "type": ["null", "string"] },
    "variants": {
      "type": ["array", "null"],
      "items": {
        "type": "object",
        "additionalProperties": false,
        "properties": {
          "name": { "type": "string" },
          "rarity": { "type": ["null", "string"] },
          "bonus": { "type": ["integer", "null"], "format": "int32" },
          "description": { "type": ["null", "string"] }
        }
      }
    }
  }
}
```

- [x] **Step 5: Run tests to verify they pass**

```bash
dotnet test DndMcpAICsharpFun.Tests/ -q
```
Expected: all passing.

- [x] **Step 6: Commit**

```bash
git add Schemas/canonical/ItemFields.schema.json Schemas/canonical/MagicItemFields.schema.json \
  DndMcpAICsharpFun.Tests/Fixtures/canonical/test-book.json \
  DndMcpAICsharpFun.Tests/Entities/Schemas/SchemaGenerationTests.cs
git commit -m "feat(schemas): align ItemFields and MagicItemFields to 5etools format"
```

---

### Task 8: God schema

**Files:**

- Modify: `Schemas/canonical/GodFields.schema.json`
- Modify: `DndMcpAICsharpFun.Tests/Fixtures/canonical/test-book.json`
- Modify: `DndMcpAICsharpFun.Tests/Entities/Schemas/SchemaGenerationTests.cs`

- [x] **Step 1: Add God fixture + InlineData**

Add to `SchemaGenerationTests`:
```csharp
[InlineData("test-book.god.pelor", "GodFields")]
```

Add to `test-book.json`:

```json
{
  "id": "test-book.god.pelor",
  "type": "God",
  "name": "Pelor",
  "sourceBook": "Test Book",
  "edition": "Edition2014",
  "page": 295,
  "firstAppearedIn": { "book": "Test Book", "edition": "Edition2014" },
  "revisedIn": [],
  "settingTags": [],
  "canonicalText": "Pelor is the god of the sun and healing, revered throughout the world.",
  "fields": {
    "alignment": ["L", "G"],
    "domains": ["Life", "Light"],
    "symbol": "Sun face",
    "pantheon": "Common",
    "plane": "Mount Celestia",
    "province": "Sun, light, strength, healing",
    "category": "Greater Deity",
    "entries": ["Pelor is the god of the sun and healing, revered throughout the world."]
  }
}
```

- [x] **Step 2: Run test to verify it fails**

```bash
dotnet test DndMcpAICsharpFun.Tests/ --filter "Generated_schema_validates_fixture_entity" -q
```
Expected: FAIL — `province`, `category`, `entries` not in current GodFields; `alignment` is now an array not a string.

- [x] **Step 3: Rewrite `GodFields.schema.json`**

```json
{
  "$schema": "http://json-schema.org/draft-04/schema#",
  "title": "GodFields",
  "type": "object",
  "additionalProperties": false,
  "required": ["alignment", "domains", "entries"],
  "properties": {
    "alignment": { "type": "array", "items": { "type": "string" } },
    "domains": { "type": "array", "items": { "type": "string" } },
    "symbol": { "type": ["null", "string"] },
    "pantheon": { "type": ["null", "string"] },
    "plane": { "type": ["null", "string"] },
    "province": { "type": ["null", "string"] },
    "category": { "type": ["null", "string"] },
    "entries": { "type": "array", "items": { "type": ["string", "object"] } }
  }
}
```

- [x] **Step 4: Run tests to verify they pass**

```bash
dotnet test DndMcpAICsharpFun.Tests/ -q
```
Expected: all passing.

- [x] **Step 5: Commit**

```bash
git add Schemas/canonical/GodFields.schema.json \
  DndMcpAICsharpFun.Tests/Fixtures/canonical/test-book.json \
  DndMcpAICsharpFun.Tests/Entities/Schemas/SchemaGenerationTests.cs
git commit -m "feat(schemas): align GodFields to 5etools format"
```

---

### Task 9: Trap + Condition + DiseasePoison + VehicleMount schemas

**Files:**

- Modify: `Schemas/canonical/TrapFields.schema.json`
- Modify: `Schemas/canonical/ConditionFields.schema.json`
- Modify: `Schemas/canonical/DiseasePoisonFields.schema.json`
- Modify: `Schemas/canonical/VehicleMountFields.schema.json`
- Modify: `DndMcpAICsharpFun.Tests/Fixtures/canonical/test-book.json`
- Modify: `DndMcpAICsharpFun.Tests/Entities/Schemas/SchemaGenerationTests.cs`

- [x] **Step 1: Add four fixtures + InlineData**

Add to `SchemaGenerationTests`:
```csharp
[InlineData("test-book.trap.pit-trap", "TrapFields")]
[InlineData("test-book.condition.blinded", "ConditionFields")]
[InlineData("test-book.disease-poison.sewer-plague", "DiseasePoisonFields")]
[InlineData("test-book.vehicle-mount.warhorse", "VehicleMountFields")]
```

Add to `test-book.json`:

```json
{
  "id": "test-book.trap.pit-trap",
  "type": "Trap",
  "name": "Pit Trap",
  "sourceBook": "Test Book",
  "edition": "Edition2014",
  "page": 122,
  "firstAppearedIn": { "book": "Test Book", "edition": "Edition2014" },
  "revisedIn": [],
  "settingTags": [],
  "canonicalText": "A pit trap consists of a hidden hole in the floor.",
  "fields": {
    "trapHazType": "MECH",
    "rating": [{ "tier": 1, "threat": "moderate" }],
    "detectDc": 15,
    "disarmDc": 15,
    "entries": ["A pit trap consists of a hidden hole in the floor covered by a trapdoor or illusion."]
  }
},
{
  "id": "test-book.condition.blinded",
  "type": "Condition",
  "name": "Blinded",
  "sourceBook": "Test Book",
  "edition": "Edition2014",
  "page": 290,
  "firstAppearedIn": { "book": "Test Book", "edition": "Edition2014" },
  "revisedIn": [],
  "settingTags": [],
  "canonicalText": "A blinded creature can't see and automatically fails any ability check that requires sight.",
  "fields": {
    "entries": [
      { "type": "list", "items": [
        "A blinded creature can't see and automatically fails any ability check that requires sight.",
        "Attack rolls against the creature have advantage, and the creature's attack rolls have disadvantage."
      ]}
    ]
  }
},
{
  "id": "test-book.disease-poison.sewer-plague",
  "type": "DiseasePoison",
  "name": "Sewer Plague",
  "sourceBook": "Test Book",
  "edition": "Edition2014",
  "page": 257,
  "firstAppearedIn": { "book": "Test Book", "edition": "Edition2014" },
  "revisedIn": [],
  "settingTags": [],
  "canonicalText": "Sewer plague is a filth disease that incubates in sewers and refuse heaps.",
  "fields": {
    "poisonTypes": ["contact"],
    "entries": ["Sewer plague is a filth disease that incubates in sewers and refuse heaps. Wererats and otyughs might spread the disease to those they bite."]
  }
},
{
  "id": "test-book.vehicle-mount.warhorse",
  "type": "VehicleMount",
  "name": "Warhorse",
  "sourceBook": "Test Book",
  "edition": "Edition2014",
  "page": 157,
  "firstAppearedIn": { "book": "Test Book", "edition": "Edition2014" },
  "revisedIn": [],
  "settingTags": [],
  "canonicalText": "A warhorse is bred for combat with a speed of 60 feet.",
  "fields": {
    "vehicleType": "CREATURE",
    "speed": { "walk": 60 },
    "capacity": "480 lb.",
    "entries": ["A warhorse is bred for combat and can be ridden into battle."]
  }
}
```

- [x] **Step 2: Run tests to verify they fail**

```bash
dotnet test DndMcpAICsharpFun.Tests/ --filter "Generated_schema_validates_fixture_entity" -q
```
Expected: all 4 new rows FAIL.

- [x] **Step 3: Rewrite `TrapFields.schema.json`**

```json
{
  "$schema": "http://json-schema.org/draft-04/schema#",
  "title": "TrapFields",
  "type": "object",
  "additionalProperties": false,
  "properties": {
    "trapHazType": { "type": ["null", "string"] },
    "rating": {
      "type": ["array", "null"],
      "items": {
        "type": "object",
        "properties": {
          "tier": { "type": "integer", "format": "int32" },
          "threat": { "type": "string" }
        }
      }
    },
    "detectDc": { "type": ["integer", "null"], "format": "int32" },
    "disarmDc": { "type": ["integer", "null"], "format": "int32" },
    "entries": { "type": ["array", "null"], "items": { "type": ["string", "object"] } },
    "variants": {
      "type": ["array", "null"],
      "items": {
        "$ref": "#/definitions/TrapVariant"
      }
    }
  },
  "definitions": {
    "TrapVariant": {
      "type": "object",
      "additionalProperties": false,
      "properties": {
        "name": { "type": "string" },
        "difficulty": { "type": ["null", "string"] },
        "detectDc": { "type": ["integer", "null"], "format": "int32" },
        "disarmDc": { "type": ["integer", "null"], "format": "int32" },
        "description": { "type": ["null", "string"] }
      }
    }
  }
}
```

- [x] **Step 4: Rewrite `ConditionFields.schema.json`**

```json
{
  "$schema": "http://json-schema.org/draft-04/schema#",
  "title": "ConditionFields",
  "type": "object",
  "additionalProperties": false,
  "required": ["entries"],
  "properties": {
    "entries": { "type": "array", "items": { "type": ["string", "object"] } },
    "conditionInflict": { "type": ["array", "null"], "items": { "type": "string" } }
  }
}
```

- [x] **Step 5: Rewrite `DiseasePoisonFields.schema.json`**

```json
{
  "$schema": "http://json-schema.org/draft-04/schema#",
  "title": "DiseasePoisonFields",
  "type": "object",
  "additionalProperties": false,
  "properties": {
    "poisonTypes": { "type": ["array", "null"], "items": { "type": "string" } },
    "entries": { "type": ["array", "null"], "items": { "type": ["string", "object"] } }
  }
}
```

- [x] **Step 6: Rewrite `VehicleMountFields.schema.json`**

```json
{
  "$schema": "http://json-schema.org/draft-04/schema#",
  "title": "VehicleMountFields",
  "type": "object",
  "additionalProperties": false,
  "properties": {
    "vehicleType": { "type": ["null", "string"] },
    "speed": { "type": ["object", "null"] },
    "capacity": { "type": ["null", "string"] },
    "entries": { "type": ["array", "null"], "items": { "type": ["string", "object"] } },
    "str": { "type": ["integer", "null"], "format": "int32" },
    "dex": { "type": ["integer", "null"], "format": "int32" },
    "con": { "type": ["integer", "null"], "format": "int32" },
    "int": { "type": ["integer", "null"], "format": "int32" },
    "wis": { "type": ["integer", "null"], "format": "int32" },
    "cha": { "type": ["integer", "null"], "format": "int32" },
    "hp": { "type": ["object", "null"] },
    "ac": { "type": ["array", "null"], "items": { "type": ["integer", "object"] } }
  }
}
```

- [x] **Step 7: Run tests to verify they pass**

```bash
dotnet test DndMcpAICsharpFun.Tests/ -q
```
Expected: all passing.

- [x] **Step 8: Commit**

```bash
git add Schemas/canonical/TrapFields.schema.json Schemas/canonical/ConditionFields.schema.json \
  Schemas/canonical/DiseasePoisonFields.schema.json Schemas/canonical/VehicleMountFields.schema.json \
  DndMcpAICsharpFun.Tests/Fixtures/canonical/test-book.json \
  DndMcpAICsharpFun.Tests/Entities/Schemas/SchemaGenerationTests.cs
git commit -m "feat(schemas): align Trap, Condition, DiseasePoison, VehicleMount to 5etools format"
```

---

### Task 10: Phase 2 custom types (Plane, Location, Faction, Lore, Rule)

**Files:**

- Modify: `Schemas/canonical/PlaneFields.schema.json`
- Modify: `Schemas/canonical/LocationFields.schema.json`
- Modify: `Schemas/canonical/FactionFields.schema.json`
- Modify: `Schemas/canonical/LoreFields.schema.json`
- Modify: `Schemas/canonical/RuleFields.schema.json`
- Modify: `DndMcpAICsharpFun.Tests/Fixtures/canonical/test-book.json`
- Modify: `DndMcpAICsharpFun.Tests/Entities/Schemas/SchemaGenerationTests.cs`

- [x] **Step 1: Add Plane, Location, Faction fixtures + InlineData; update Lore and Rule fixtures**

Add to `SchemaGenerationTests`:
```csharp
[InlineData("test-book.plane.nine-hells", "PlaneFields")]
[InlineData("test-book.location.candlekeep", "LocationFields")]
[InlineData("test-book.faction.harpers", "FactionFields")]
```

Update existing `test-book.lore.the-planes` fields in `test-book.json`:

```json
{
  "category": "Cosmology",
  "settingContext": null,
  "entries": ["The planes of existence are the realms of the multiverse, home to immortals and mortals alike."]
}
```

Update existing `test-book.rule.encounter-building` fields in `test-book.json`:

```json
{
  "ruleType": "O",
  "entries": ["To build a balanced encounter, calculate the XP budget by party level and size, then select monsters whose XP totals fall within that budget."]
}
```

Add new fixtures to `test-book.json`:

```json
{
  "id": "test-book.plane.nine-hells",
  "type": "Plane",
  "name": "The Nine Hells of Baator",
  "sourceBook": "Test Book",
  "edition": "Edition2014",
  "page": 63,
  "firstAppearedIn": { "book": "Test Book", "edition": "Edition2014" },
  "revisedIn": [],
  "settingTags": [],
  "canonicalText": "The Nine Hells of Baator is a plane of absolute law and evil.",
  "fields": {
    "category": "Outer",
    "entries": ["The Nine Hells of Baator is a plane of absolute law and evil, ruled by archdevils who answer to Asmodeus."],
    "relatedPlanes": ["Acheron", "Gehenna"]
  }
},
{
  "id": "test-book.location.candlekeep",
  "type": "Location",
  "name": "Candlekeep",
  "sourceBook": "Test Book",
  "edition": "Edition2014",
  "page": 10,
  "firstAppearedIn": { "book": "Test Book", "edition": "Edition2014" },
  "revisedIn": [],
  "settingTags": ["Forgotten Realms"],
  "canonicalText": "Candlekeep is a great library fortress on the Sword Coast.",
  "fields": {
    "category": "Library-Fortress",
    "setting": "Forgotten Realms",
    "entries": ["Candlekeep is a great library fortress on the Sword Coast, home to the greatest collection of books and scrolls in Faerûn."]
  }
},
{
  "id": "test-book.faction.harpers",
  "type": "Faction",
  "name": "The Harpers",
  "sourceBook": "Test Book",
  "edition": "Edition2014",
  "page": 21,
  "firstAppearedIn": { "book": "Test Book", "edition": "Edition2014" },
  "revisedIn": [],
  "settingTags": ["Forgotten Realms"],
  "canonicalText": "The Harpers are a secret network of spellcasters and spies who oppose the rise of tyranny.",
  "fields": {
    "headquarters": "Twilight Hall, Berdusk",
    "goals": ["Gather information", "Aid the weak", "Oppose tyrants"],
    "entries": ["The Harpers are a secret network of spellcasters and spies who oppose the rise of tyranny."]
  }
}
```

- [x] **Step 2: Run tests to verify they fail**

```bash
dotnet test DndMcpAICsharpFun.Tests/ --filter "Generated_schema_validates_fixture_entity" -q
```
Expected: FAIL for new plane/location/faction fixtures, and FAIL for lore/rule (existing fixtures now use `entries` instead of `description`, and `ruleType` instead of `category`).

- [x] **Step 3: Rewrite `PlaneFields.schema.json`**

```json
{
  "$schema": "http://json-schema.org/draft-04/schema#",
  "title": "PlaneFields",
  "type": "object",
  "additionalProperties": false,
  "required": ["category", "entries"],
  "properties": {
    "category": { "type": "string" },
    "entries": { "type": "array", "items": { "type": ["string", "object"] } },
    "relatedPlanes": { "type": ["array", "null"], "items": { "type": "string" } }
  }
}
```

- [x] **Step 4: Rewrite `LocationFields.schema.json`**

```json
{
  "$schema": "http://json-schema.org/draft-04/schema#",
  "title": "LocationFields",
  "type": "object",
  "additionalProperties": false,
  "required": ["entries"],
  "properties": {
    "category": { "type": ["null", "string"] },
    "setting": { "type": ["null", "string"] },
    "entries": { "type": "array", "items": { "type": ["string", "object"] } }
  }
}
```

- [x] **Step 5: Rewrite `FactionFields.schema.json`**

```json
{
  "$schema": "http://json-schema.org/draft-04/schema#",
  "title": "FactionFields",
  "type": "object",
  "additionalProperties": false,
  "required": ["entries"],
  "properties": {
    "headquarters": { "type": ["null", "string"] },
    "goals": { "type": ["array", "null"], "items": { "type": "string" } },
    "entries": { "type": "array", "items": { "type": ["string", "object"] } }
  }
}
```

- [x] **Step 6: Rewrite `LoreFields.schema.json`**

```json
{
  "$schema": "http://json-schema.org/draft-04/schema#",
  "title": "LoreFields",
  "type": "object",
  "additionalProperties": false,
  "required": ["category", "entries"],
  "properties": {
    "category": { "type": "string" },
    "settingContext": { "type": ["null", "string"] },
    "entries": { "type": "array", "items": { "type": ["string", "object"] } }
  }
}
```

- [x] **Step 7: Rewrite `RuleFields.schema.json`**

```json
{
  "$schema": "http://json-schema.org/draft-04/schema#",
  "title": "RuleFields",
  "type": "object",
  "additionalProperties": false,
  "required": ["entries"],
  "properties": {
    "ruleType": { "type": ["null", "string"] },
    "entries": { "type": "array", "items": { "type": ["string", "object"] } }
  }
}
```

- [x] **Step 8: Run tests to verify they pass**

```bash
dotnet test DndMcpAICsharpFun.Tests/ -q
```
Expected: all passing.

- [x] **Step 9: Commit**

```bash
git add Schemas/canonical/PlaneFields.schema.json Schemas/canonical/LocationFields.schema.json \
  Schemas/canonical/FactionFields.schema.json Schemas/canonical/LoreFields.schema.json \
  Schemas/canonical/RuleFields.schema.json \
  DndMcpAICsharpFun.Tests/Fixtures/canonical/test-book.json \
  DndMcpAICsharpFun.Tests/Entities/Schemas/SchemaGenerationTests.cs
git commit -m "feat(schemas): align Phase 2 custom types to 5etools conventions"
```

---

### Task 11: Update ExtractionPromptBuilder

**Files:**

- Modify: `Features/Ingestion/EntityExtraction/ExtractionPromptBuilder.cs`
- Modify: `DndMcpAICsharpFun.Tests/Entities/Extraction/ExtractionPromptBuilderTests.cs`

- [x] **Step 1: Write failing tests**

Add to `ExtractionPromptBuilderTests`:

```csharp
[Fact]
public void Spell_prompt_contains_school_code_table()
{
    var b = new ExtractionPromptBuilder();
    var prompt = b.BuildSystemPrompt("PHB", "Edition2014", EntityType.Spell);
    prompt.Should().Contain("EV=Evocation")
          .And.Contain("C=Conjuration")
          .And.Contain("\"school\": \"EV\"");
}

[Fact]
public void Monster_prompt_contains_size_and_alignment_code_tables()
{
    var b = new ExtractionPromptBuilder();
    var prompt = b.BuildSystemPrompt("MM", "Edition2014", EntityType.Monster);
    prompt.Should().Contain("M=Medium")
          .And.Contain("L=Lawful")
          .And.Contain("\"size\": [\"M\"]");
}

[Fact]
public void All_prompts_contain_entries_format_guidance()
{
    var b = new ExtractionPromptBuilder();
    foreach (var type in Enum.GetValues<EntityType>())
    {
        var prompt = b.BuildSystemPrompt("Test", "Edition2014", type);
        prompt.Should().Contain("entries", because: $"{type} prompt should mention entries array");
    }
}

[Fact]
public void Rule_prompt_contains_ruleType_codes()
{
    var b = new ExtractionPromptBuilder();
    var prompt = b.BuildSystemPrompt("DMG", "Edition2014", EntityType.Rule);
    prompt.Should().Contain("ruleType")
          .And.Contain("O=Optional")
          .And.Contain("V=Variant");
}

[Fact]
public void God_prompt_contains_alignment_code_table()
{
    var b = new ExtractionPromptBuilder();
    var prompt = b.BuildSystemPrompt("MTF", "Edition2014", EntityType.God);
    prompt.Should().Contain("L=Lawful")
          .And.Contain("G=Good")
          .And.Contain("\"alignment\": [\"L\", \"G\"]");
}
```

- [x] **Step 2: Run tests to verify they fail**

```bash
dotnet test DndMcpAICsharpFun.Tests/ --filter "ExtractionPromptBuilderTests" -q
```
Expected: 5 new tests FAIL, 3 existing tests PASS.

- [x] **Step 3: Rewrite `ExtractionPromptBuilder.cs`**

```csharp
using System.Text;
using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

public sealed class ExtractionPromptBuilder
{
    private const string SizeCodes =
        "Size codes: T=Tiny, S=Small, M=Medium, L=Large, H=Huge, G=Gargantuan. " +
        "Use an array: \"size\": [\"M\"]";

    private const string AlignmentCodes =
        "Alignment codes: L=Lawful, C=Chaotic, G=Good, E=Evil, N=Neutral, U=Unaligned, A=Any. " +
        "Use an array: \"alignment\": [\"L\", \"G\"] for Lawful Good.";

    private const string SchoolCodes =
        "Spell school codes: A=Abjuration, C=Conjuration, D=Divination, EN=Enchantment, " +
        "EV=Evocation, I=Illusion, N=Necromancy, T=Transmutation. " +
        "Example: \"school\": \"EV\"";

    private const string RuleTypeCodes =
        "Rule type codes: C=Core rule, O=Optional rule, V=Variant rule. " +
        "Example: \"ruleType\": \"O\"";

    private const string EntriesGuidance =
        "Produce descriptive text as a JSON `entries` array. " +
        "Plain paragraphs are strings. Named subsections use: " +
        "{\"type\":\"entries\",\"name\":\"Section Name\",\"entries\":[\"...\"]}. " +
        "Lists use: {\"type\":\"list\",\"items\":[\"item1\",\"item2\"]}. " +
        "Use inline tags where appropriate: {@damage 2d8} for damage rolls, " +
        "{@dc 15} for DCs, {@condition prone} for conditions, " +
        "{@item Javelin|PHB} for item references.";

    public string BuildSystemPrompt(string sourceBook, string edition, EntityType type)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are extracting structured D&D rules data from official rulebook text.");
        sb.AppendLine($"Source book: {sourceBook} ({edition}).");
        sb.AppendLine($"Entity type: {type}.");
        sb.AppendLine();
        sb.AppendLine($"Call the tool `{ToolName(type)}` with a JSON object that conforms exactly to its input_schema.");
        sb.AppendLine("Do not include any prose. The tool's input is the only output we read.");
        sb.AppendLine("If the source text is incomplete or ambiguous, leave optional fields null/absent rather than guessing.");
        sb.AppendLine("The source text may contain OCR artifacts (e.g. 'gons' → 'gods', 'lhe' → 'the'). Use surrounding context to infer the correct meaning.");
        sb.AppendLine("Cross-entity references must use existing slug-style IDs of form `<book-slug>.<type-slug>.<entity-slug>`.");
        sb.AppendLine();
        sb.AppendLine("Do not extract chapter titles, section headings, or table headers as entities. Only extract named, discrete game elements.");
        sb.AppendLine();
        sb.AppendLine(EntriesGuidance);
        sb.AppendLine();
        sb.AppendLine("Type routing guidance:");
        sb.AppendLine("- Use `Lore` for named worldbuilding concepts, cosmology, pantheon overviews, philosophies, and cultural flavour that is not a discrete game entity.");
        sb.AppendLine("- Use `Rule` for mechanical procedures, encounter tables, adventure design guidelines, random tables, and DMing system explanations.");
        sb.AppendLine("- Use `God` only when the entity is a named deity with known alignment and at least one domain.");
        sb.AppendLine("- Use `Plane` only when the entity is a named plane of existence with a defined category (Inner, Outer, Transitive, Material, etc.).");
        sb.AppendLine("- Use `Monster` only when the entity has a stat block with a challenge rating.");
        sb.AppendLine();

        switch (type)
        {
            case EntityType.Spell:
                sb.AppendLine(SchoolCodes);
                sb.AppendLine("Casting time example: \"time\": [{\"number\": 1, \"unit\": \"action\"}]");
                sb.AppendLine("Range example: \"range\": {\"type\": \"point\", \"distance\": {\"type\": \"feet\", \"amount\": 150}}");
                sb.AppendLine("Duration example: \"duration\": [{\"type\": \"instant\"}] or [{\"type\": \"timed\", \"duration\": {\"type\": \"minute\", \"amount\": 1}, \"concentration\": true}]");
                break;

            case EntityType.Monster:
                sb.AppendLine(SizeCodes);
                sb.AppendLine(AlignmentCodes);
                sb.AppendLine("Type example: \"type\": {\"type\": \"humanoid\", \"tags\": [\"bullywug\"]}");
                sb.AppendLine("AC example: \"ac\": [15] or \"ac\": [{\"ac\": 17, \"from\": [\"natural armor\"]}]");
                sb.AppendLine("HP example: \"hp\": {\"average\": 11, \"formula\": \"2d8+2\"}");
                sb.AppendLine("Ability scores are flat fields: \"str\": 12, \"dex\": 14, \"con\": 10, \"int\": 8, \"wis\": 10, \"cha\": 8");
                sb.AppendLine("Skills example: \"skill\": {\"perception\": \"+5\", \"stealth\": \"+3\"}");
                sb.AppendLine("CR example: \"cr\": \"1/4\"");
                sb.AppendLine("Traits/actions use entries: \"trait\": [{\"name\": \"Amphibious\", \"entries\": [\"Can breathe air and water.\"]}]");
                break;

            case EntityType.Race:
            case EntityType.Subrace:
                sb.AppendLine(SizeCodes);
                sb.AppendLine("Speed example: \"speed\": {\"walk\": 30, \"fly\": 30}");
                sb.AppendLine("Ability bonuses example: \"ability\": [{\"str\": 2, \"dex\": 1}]");
                sb.AppendLine("Language proficiencies example: \"languageProficiencies\": [{\"common\": true, \"anyStandard\": 1}]");
                break;

            case EntityType.God:
                sb.AppendLine(AlignmentCodes);
                sb.AppendLine("Example: \"alignment\": [\"L\", \"G\"] for Lawful Good");
                break;

            case EntityType.Rule:
                sb.AppendLine(RuleTypeCodes);
                break;

            case EntityType.MagicItem:
                sb.AppendLine("If the source text describes multiple tiers of the same item (e.g. +1/+2/+3), extract them as a single entity with a `variants` array.");
                break;

            case EntityType.Trap:
                sb.AppendLine("If the source text describes a group of trap variants (e.g. Simple Pit / Spiked Pit), extract them as a single entity with a `variants` array.");
                sb.AppendLine("trapHazType codes: MECH=Mechanical, MAG=Magical, SMPL=Simple, CMPX=Complex");
                break;
        }

        return sb.ToString();
    }

    public string BuildUserPrompt(EntityCandidate candidate)
    {
        var pageNote = candidate.Page is { } p ? $" (page {p})" : "";
        var sb = new StringBuilder();
        sb.AppendLine($"Entity: {candidate.DisplayName}{pageNote}");
        sb.AppendLine();
        sb.AppendLine("Source text:");
        sb.AppendLine("```");
        sb.Append(candidate.Text);
        sb.AppendLine();
        sb.AppendLine("```");
        return sb.ToString();
    }

    public string ToolName(EntityType type)
    {
        var camel = type.ToString();
        var sb = new StringBuilder("emit_");
        for (int i = 0; i < camel.Length; i++)
        {
            var c = camel[i];
            if (i > 0 && char.IsUpper(c)) sb.Append('_');
            sb.Append(char.ToLowerInvariant(c));
        }
        sb.Append("_fields");
        return sb.ToString();
    }

    public string ToolDescription(EntityType type) =>
        $"Emit a structured {type} entity's `fields` object. The input MUST validate against the provided schema.";
}
```

- [x] **Step 4: Run tests to verify they pass**

```bash
dotnet test DndMcpAICsharpFun.Tests/ -q
```
Expected: all passing.

- [x] **Step 5: Commit**

```bash
git add Features/Ingestion/EntityExtraction/ExtractionPromptBuilder.cs \
  DndMcpAICsharpFun.Tests/Entities/Extraction/ExtractionPromptBuilderTests.cs
git commit -m "feat(extraction): add 5etools code tables and entries format guidance to system prompts"
```

---

### Task 12: Migrate phb14.json sample data

**Files:**

- Modify: `data/canonical/phb14.json`

- [x] **Step 1: Read the current phb14.json structure**

```bash
cat data/canonical/phb14.json | python3 -m json.tool | head -20
```
This shows whether it is a bare array or a `{entities:[]}` wrapper.

- [x] **Step 2: Rewrite the 3 sample entities to 5etools format**

Replace `phb14.json` content. Keep the outer structure (array or wrapper) as-is, just update the `fields` of each entity:

**phb14.class.fighter** — update `fields` to:
```json
{
  "hd": { "number": 1, "faces": 10 },
  "proficiency": ["str", "con"],
  "startingProficiencies": {
    "armor": ["light", "medium", "heavy", "shields"],
    "weapons": ["simple", "martial"],
    "skills": { "choose": { "from": ["Acrobatics","Animal Handling","Athletics","History","Insight","Intimidation","Perception","Survival"], "count": 2 } }
  },
  "classFeatures": [
    { "classFeature": "Fighting Style|Fighter|PHB|1", "level": 1 },
    { "classFeature": "Second Wind|Fighter|PHB|1", "level": 1 },
    { "classFeature": "Action Surge|Fighter|PHB|2", "level": 2 },
    { "classFeature": "Extra Attack|Fighter|PHB|5", "level": 5 }
  ],
  "entries": ["A master of martial combat, skilled with a variety of weapons and armor."]
}
```

**phb14.monster.aboleth** — update `fields` to:
```json
{
  "size": ["L"],
  "type": { "type": "aberration", "tags": [] },
  "alignment": ["L", "E"],
  "ac": [{ "ac": 17, "from": ["natural armor"] }],
  "hp": { "average": 135, "formula": "18d10+36" },
  "speed": { "walk": 10, "swim": 40 },
  "str": 21, "dex": 9, "con": 15, "int": 18, "wis": 15, "cha": 18,
  "save": { "con": "+6", "int": "+8", "wis": "+6", "cha": "+8" },
  "skill": { "history": "+12", "perception": "+10" },
  "senses": ["darkvision 120 ft."],
  "passive": 20,
  "languages": ["Deep Speech", "telepathy 120 ft."],
  "cr": "10",
  "trait": [
    { "name": "Amphibious", "entries": ["The aboleth can breathe air and water."] },
    { "name": "Mucous Cloud", "entries": ["While underwater, the aboleth is surrounded by transformative mucus."] },
    { "name": "Probing Telepathy", "entries": ["If a creature communicates telepathically with the aboleth, the aboleth learns the creature's greatest desires."] }
  ],
  "action": [
    { "name": "Multiattack", "entries": ["The aboleth makes three tentacle attacks."] },
    { "name": "Tentacle", "entries": ["{@atk mw} {@hit 9} to hit, reach 10 ft., one target. {@h}{@damage 2d6+5} bludgeoning damage. If the target is a creature, it must succeed on a {@dc 14} Constitution saving throw or become diseased."] },
    { "name": "Tail", "entries": ["{@atk mw} {@hit 9} to hit, reach 10 ft., one target. {@h}{@damage 3d6+5} bludgeoning damage."] },
    { "name": "Enslave", "entries": ["The aboleth targets one creature it can see within 30 ft. of it. The target must succeed on a {@dc 14} Wisdom saving throw or be magically charmed by the aboleth until the aboleth dies or until it is on a different plane of existence from the target."] }
  ],
  "legendary": [
    { "name": "Detect", "entries": ["The aboleth makes a Wisdom ({@skill Perception}) check."] },
    { "name": "Tail Swipe", "entries": ["The aboleth makes one tail attack."] },
    { "name": "Psychic Drain", "entries": ["One creature charmed by the aboleth takes {@damage 3d6} psychic damage, and the aboleth regains hit points equal to the damage the creature takes."] }
  ],
  "legendaryHeader": ["The aboleth can take 3 legendary actions, choosing from the options below."],
  "environment": ["underwater"]
}
```

**phb14.spell.fireball** — update `fields` to:
```json
{
  "level": 3,
  "school": "EV",
  "time": [{ "number": 1, "unit": "action" }],
  "range": { "type": "point", "distance": { "type": "feet", "amount": 150 } },
  "components": { "v": true, "s": true, "m": true, "material": "a tiny ball of bat guano and sulfur" },
  "duration": [{ "type": "instant" }],
  "ritual": false,
  "concentration": false,
  "entries": [
    "A bright streak flashes from your pointing finger to a point you choose within range and then blossoms with a low roar into an explosion of flame. Each creature in a 20-foot-radius sphere centered on that point must make a Dexterity saving throw. A target takes {@damage 8d6} fire damage on a failed save, or half as much damage on a successful one.",
    "The fire spreads around corners. It ignites flammable objects in the area that aren't being worn or carried."
  ],
  "entriesHigherLevel": ["When you cast this spell using a spell slot of 4th level or higher, the damage increases by {@damage 1d6} for each slot level above 3rd."],
  "damageInflict": ["fire"],
  "savingThrow": ["dexterity"],
  "classes": ["Sorcerer", "Wizard"]
}
```

- [x] **Step 3: Validate against schemas manually**

```bash
dotnet test DndMcpAICsharpFun.Tests/ -q
```
Expected: all still passing (phb14.json is not used by tests, so this is a sanity check that nothing broke).

- [x] **Step 4: Commit**

```bash
git add data/canonical/phb14.json
git commit -m "chore(data): migrate phb14.json sample entities to 5etools format"
```

---

## Self-Review

**Spec coverage:**

- ✅ Phase 1 — 17 5etools-equivalent types: Spell (T1), Monster (T2), Race/Subrace (T3), Class/Subclass (T4), Background/Feat (T5), Weapon/Armor (T6), Item/MagicItem (T7), God (T8), Trap/Condition/DiseasePoison/VehicleMount (T9)
- ✅ Phase 2 — 5 custom types: Plane/Location/Faction/Lore/Rule (T10)
- ✅ Core conventions: `entries[]`, code values, flat ability scores, `cr` string — all applied per type
- ✅ Inline tags preserved in `fields` — schemas allow `["string", "object"]` items in entries
- ✅ Extraction prompt code tables + entries guidance — T11
- ✅ phb14.json migration — T12
- ✅ No C# application code changes — confirmed, fields are opaque throughout
- ✅ `additionalProperties: false` retained on all schemas for strict validation
- ✅ Required fields retained where meaningful (Monster: cr/size/type; God: alignment/domains/entries; custom types: entries)

**Placeholder scan:** None found. All task steps contain actual JSON or code.

**Type consistency:** `entries` array format used identically across all schemas. `["string", "object"]` items type used consistently for entries items. Fixture IDs follow `test-book.<type-slug>.<name-slug>` pattern throughout.
