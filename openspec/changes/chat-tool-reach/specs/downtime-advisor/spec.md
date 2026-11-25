## MODIFIED Requirements

### Requirement: Grounded, cited downtime-planning tool

The system SHALL expose a per-session chat tool `plan_downtime(activity)` that retrieves
downtime passages scoped to the downtime source books and returns them each with its citation (source
book and section or title). The tool SHALL NOT be ownership-gated and SHALL NOT accept a user or
campaign id as an argument (downtime rules are universal). Its contract SHALL require the plan to be
composed only from the returned cited passages — the activity's time cost, gold cost, and outcome —
citing each, and to state that the rules do not detail the activity when no relevant passages are
returned, never inventing times or costs. Retrieval SHALL NOT be restricted by edition.

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
