## ADDED Requirements

### Requirement: Downtime retrieval is scoped to the downtime source books

The system SHALL provide a fixed set of downtime source books (Xanathar's Guide to Everything and the
Dungeon Master's Guide) whose values equal the real `dnd_blocks` source-book payload, and SHALL scope
downtime retrieval to that set using the source-book filter so that non-downtime prose is excluded
from the retrieved passages. Retrieval SHALL request enough passages that an activity spanning more
than one rule can surface each relevant passage.

#### Scenario: Downtime retrieval excludes out-of-scope prose

- **WHEN** a downtime activity is retrieved for planning
- **THEN** the returned passages SHALL come only from the downtime source books and SHALL exclude blocks from other books

#### Scenario: Retrieval requests enough breadth

- **WHEN** an activity's rule spans more than one passage (e.g. crafting cost and crafting time)
- **THEN** retrieval SHALL request enough passages that each relevant passage can appear in the result set

### Requirement: Grounded, cited downtime-planning tool

The system SHALL expose a per-session chat tool `plan_downtime(activity, edition?)` that retrieves
downtime passages scoped to the downtime source books and returns them each with its citation (source
book and section or title). The tool SHALL NOT be ownership-gated and SHALL NOT accept a user or
campaign id as an argument (downtime rules are universal). Its contract SHALL require the plan to be
composed only from the returned cited passages — the activity's time cost, gold cost, and outcome —
citing each, and to state that the rules do not detail the activity when no relevant passages are
returned, never inventing times or costs. When no `edition` is supplied, retrieval SHALL NOT be
restricted by edition.

#### Scenario: Downtime activity returns cited passages

- **WHEN** `plan_downtime` is called with an activity the rules cover (e.g. crafting armour)
- **THEN** it SHALL return passages drawn only from the downtime source books, each carrying its citation

#### Scenario: Plan composed only from retrieved rules

- **WHEN** the tool returns downtime passages for an activity
- **THEN** the contract SHALL require the plan (time cost, gold cost, outcome) to be composed from those passages and cite each, not invented

#### Scenario: Honest empty when the rules do not cover it

- **WHEN** the scoped retrieval returns no relevant passages
- **THEN** the tool SHALL return an explicit empty result and the contract SHALL require stating the rules do not detail the activity, not an invented plan

#### Scenario: Tool schema does not expose a user or campaign id

- **WHEN** the `plan_downtime` tool schema is inspected
- **THEN** it SHALL NOT expose a `userId` or `campaignId` argument
