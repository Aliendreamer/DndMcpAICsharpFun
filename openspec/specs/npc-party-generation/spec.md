# npc-party-generation Specification

## Purpose
TBD - created by archiving change npc-party-generation. Update Purpose after archive.
## Requirements
### Requirement: Deterministic theme→ensemble templates

`NpcPartyTemplates.Resolve(theme)` SHALL return a named ensemble roster (an ordered list of
`(Role, Archetype)`, leader first) chosen by deterministic case-insensitive keyword match against the
theme, falling back to a Default roster when no keyword matches. Every archetype in every roster
(including Default) MUST be a member of `NpcArchetypes.Common`. The theme MUST NOT be used as a
monster/keyword search filter.

#### Scenario: Keyword selects the matching template

- **WHEN** `Resolve("a Sharn heist crew")` is called
- **THEN** it returns the criminal roster led by `Bandit Captain` (with Thug/Thug/Spy), and the template
  name identifies it as the criminal template

#### Scenario: Unmatched theme falls back to Default

- **WHEN** `Resolve("a quiet afternoon in the meadow")` is called (no template keyword matches)
- **THEN** it returns the Default roster (`Veteran` leader + Guard/Guard/Commoner), never an empty roster

#### Scenario: All roster archetypes are grounded roster members

- **WHEN** every template roster (including Default) is enumerated
- **THEN** each archetype it names is a member of `NpcArchetypes.Common`

### Requirement: Party generation grounds every member

`NpcGenerationService.GeneratePartyAsync(theme, ct)` SHALL resolve the template for the theme and, for
each roster entry, produce a grounded NPC via the existing anti-fuzzy archetype-name resolution (a real
Monster stat block, with the per-member not-in-corpus flag), returning a `GeneratedNpcParty` carrying
the theme, the template name, and the ordered members with their roles.

#### Scenario: Ensemble members carry real stat blocks

- **WHEN** `GeneratePartyAsync("criminal underworld")` runs against a corpus containing the roster's
  Monster entities
- **THEN** the result has one member per roster entry, in roster order, each with its `Role` and a
  `GeneratedNpc` whose `StatBlock` is populated from the real entity and `ArchetypeInCorpus` is true

#### Scenario: A missing archetype is flagged, not fatal

- **WHEN** a roster archetype is absent from the corpus
- **THEN** that member's `GeneratedNpc.ArchetypeInCorpus` is false (with `AvailableArchetypes` populated)
  and the party is still returned with the remaining members intact

### Requirement: Ownership-free single-param party chat tool

The chat surface SHALL expose `generate_npc_party(theme)` — a single required string parameter,
registered in the authenticated block but NOT gated on campaign ownership (no `userId`/`campaignId`).
Its description SHALL instruct the model to name/flavor each member to the theme, take all mechanical
stats from the returned blocks and cite them, never invent stats, and drop/replace any member that is
not in the corpus.

#### Scenario: Tool is present for an authenticated session and exposes no ownership args

- **WHEN** the tool list is built for an authenticated user
- **THEN** it contains `generate_npc_party`, and that tool's schema exposes exactly one string parameter
  (`theme`) and no `userId`/`campaignId` argument

#### Scenario: Tool returns the grounded ensemble

- **WHEN** `generate_npc_party` is invoked with `theme: "temple cult"`
- **THEN** it returns the cult ensemble (Cult Fanatic leader + Cultist/Cultist/Acolyte), each member
  carrying its real stat block

