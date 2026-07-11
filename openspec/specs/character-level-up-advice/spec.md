# character-level-up-advice Specification

## Purpose
TBD - created by archiving change character-level-up-advice. Update Purpose after archive.
## Requirements
### Requirement: Rule-grounded level-up delta for an owned hero

The system SHALL compute, for a hero snapshot the calling user owns and a chosen class advancement, a
deterministic level-up delta describing what the character gains at the next level: the new level, the
hit-point gain (hit-die average, with a rolled option available), the proficiency-bonus change, the
spell-slot change, the features gained (each carrying its cited source), and which open choices the level
unlocks (ability-score-or-feat, subclass when due, and newly-available spells for casters). The delta
SHALL be derived from the class entity's rule data (hit dice, the proficiency-bonus formula, the shared
spell-slot logic, and the parsed per-level class/subclass features), NOT from model free-text.

#### Scenario: Advancing a class produces a grounded delta

- **WHEN** the owner requests a level-up plan for a class their hero already has
- **THEN** the delta SHALL report the HP gain, the proficiency bonus before and after, the spell-slot change, and the features gained at the new level, each feature carrying a cited source

#### Scenario: A level with no new slots reports no slot change

- **WHEN** the advanced class grants no additional spell slots at the new level
- **THEN** the delta's spell-slot change SHALL be empty rather than fabricated

### Requirement: Option menus are real cited entities, never invented

For each open choice a level unlocks, the system SHALL offer a menu of options drawn from the typed entity
store (`dnd_entities`): subclasses for the class, feats for the edition, and the newly-available spells for
the class. Every option SHALL carry its entity id, name, and source. The system SHALL NOT present an option
that is not a real entity record.

#### Scenario: Subclass-selection level lists real subclasses

- **WHEN** the delta's open choices include a subclass selection for the advanced class
- **THEN** the option menu SHALL list subclasses that belong to that class, each with its id, name, and source

#### Scenario: An ability-score-improvement level offers real feats

- **WHEN** the delta's open choices include ability-score-or-feat
- **THEN** the option menu SHALL include feats that exist in the entity store for the character's edition, each cited

### Requirement: Recommend a pick with reasons, constrained to the cited options

The system SHALL expose a per-user chat tool `plan_level_up(heroSnapshotId, targetClass?, considerDip?)`
that returns the level-up advice for the signed-in user's hero. The assistant SHALL be able to recommend a
specific choice with reasons that reference the character's own sheet, and its recommendation SHALL be
constrained to the cited options the tool returned — it SHALL NOT recommend an option not present in that
menu. When `targetClass` is omitted, advice SHALL be returned for every class the hero already has.

#### Scenario: The assistant recommends from the returned menu

- **WHEN** the assistant recommends a feat, subclass, or spell for a level-up
- **THEN** the recommended option SHALL be one of the cited options the tool returned for that choice

#### Scenario: Unauthenticated caller gets no tool

- **WHEN** the request has no authenticated user identity
- **THEN** the `plan_level_up` tool SHALL NOT be offered

### Requirement: Advance an existing class or recommend a new-class dip

The system SHALL support planning the advancement of a class the hero already has (single-class, or a chosen
class of a multiclass hero) and, when a dip is considered, SHALL evaluate taking the first level in a new
class. For a new-class dip candidate the system SHALL fold in the existing multiclass-validity check so an
illegal dip is reported as not allowed (with the failed prerequisite) rather than recommended.

#### Scenario: A legal dip is offered as a candidate

- **WHEN** a dip into a new class is considered and the hero meets that class's multiclass prerequisites
- **THEN** the advice SHALL include the dip as a candidate advancement with its own grounded delta and option menus

#### Scenario: An illegal dip is reported, not recommended

- **WHEN** a dip into a new class is considered and the hero fails that class's ability-score prerequisite
- **THEN** the advice SHALL mark the dip not allowed and identify the failed prerequisite, and the assistant SHALL NOT recommend it

### Requirement: Level-up planning is authorized by the calling user's identity

The level-up advice service SHALL authorize by the calling user's identity: it SHALL only plan for a hero
snapshot owned by that user, resolved from the trusted session, and SHALL reject a request for a snapshot
owned by a different user rather than leak or act on it.

#### Scenario: Planning another user's hero is rejected

- **WHEN** the service is asked to plan a hero snapshot owned by a different user
- **THEN** it SHALL throw rather than return that hero's plan

### Requirement: HeroDetail shows the grounded delta and hands off to chat for the recommendation

The HeroDetail page SHALL offer a "Plan level-up" action that renders the deterministic level-up delta as an
inline grounded card (the rule-grounded facts, with no language-model call), and SHALL provide a way to ask
the assistant for an opinionated recommendation that continues in the chat surface.

#### Scenario: The grounded card renders without a model call

- **WHEN** the owner triggers "Plan level-up" for their hero on HeroDetail
- **THEN** the page SHALL render the level-up delta (HP, proficiency bonus, slot change, features gained, and the open choices) as a display-only card, and SHALL offer an action that continues the recommendation in chat

