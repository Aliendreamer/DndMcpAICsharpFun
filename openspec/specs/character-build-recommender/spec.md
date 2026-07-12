# character-build-recommender Specification

## Purpose
TBD - created by archiving change character-build-recommender. Update Purpose after archive.
## Requirements
### Requirement: Concept-to-build option package grounded in cited entities

The system SHALL, for a chosen class and a text concept, return a grounded build-option package: the
class's structured build info (hit die, spellcasting ability, save proficiencies, subclass title) and
cited option menus for the build's open choices — subclasses of that class, feats, and concept-relevant
spells — each option carrying its entity id, name, and source. The assistant SHALL recommend the subclass,
key feats, and signature spells ONLY from the returned cited options, and SHALL NOT name a subclass, feat,
or spell that is not a real entity record.

#### Scenario: A valid class yields structured info and cited menus

- **WHEN** the recommender runs for a class that exists in the corpus and a concept
- **THEN** it SHALL return the class's structured build info and cited subclass, feat, and spell option menus

#### Scenario: The recommendation is constrained to the cited menus

- **WHEN** the assistant recommends a subclass, feat, or spell for the build
- **THEN** the recommended option SHALL be one of the cited options the tool returned

### Requirement: The class is validated to exist; unknown classes return the available set

The system SHALL match the concept to a class as the assistant's judgment and SHALL validate that the
chosen class exists in the entity store for the edition. When the class is not in the corpus, the system
SHALL return a not-in-corpus result together with the list of class names that ARE available, so the
assistant re-picks a real class rather than recommending an absent one.

#### Scenario: An out-of-corpus class is rejected with alternatives

- **WHEN** the assistant proposes a class not present in the entity store (e.g. a class from a book not ingested)
- **THEN** the result SHALL indicate the class is not in the corpus and SHALL list the available class names, and the assistant SHALL NOT recommend the absent class

### Requirement: Feats and spells are retrieved by the concept

The system SHALL retrieve the feat and spell option menus using the concept as the retrieval query, so the
cited options are relevant to the concept (e.g. control-oriented spells and feats for a "battlefield
controller"). The target level, when supplied, SHALL bound the reachable spell levels. Subclass options
SHALL be all subclasses of the chosen class.

#### Scenario: Concept-relevant spell options

- **WHEN** the recommender runs for a caster class with a control-themed concept
- **THEN** the returned spell options SHALL be drawn from a concept-relevant retrieval, not an arbitrary or unfiltered list

### Requirement: Single-class builds; multiclass concepts defer to the level-up assistant

The recommender SHALL produce a single-class build identity. When the concept implies multiclassing, the
assistant SHALL recommend the primary class and note a dip direction, deferring the actual dip planning to
the level-up assistant rather than sequencing a multiclass path here.

#### Scenario: A multiclass concept yields a single-class identity plus a dip note

- **WHEN** the concept implies a multiclass build (e.g. "a fighter who casts spells")
- **THEN** the recommendation SHALL name a single primary class and MAY note a dip direction, and SHALL NOT sequence a full multiclass path

### Requirement: A per-user chat tool exposes the recommender without ownership gating

The system SHALL expose a per-user chat tool `recommend_build(className, concept, targetLevel?)` in the
authenticated tool set. Because the recommender touches no owned data, the tool SHALL NOT take a user-id
argument and SHALL NOT be ownership-gated. Unauthenticated callers SHALL NOT be offered the tool.

#### Scenario: The tool is offered only to authenticated callers and takes no user id

- **WHEN** an authenticated user asks the assistant to recommend a build
- **THEN** the `recommend_build` tool SHALL be available, and its schema SHALL NOT expose a user-id argument

#### Scenario: Unauthenticated caller gets no tool

- **WHEN** the request has no authenticated user identity
- **THEN** the `recommend_build` tool SHALL NOT be offered

