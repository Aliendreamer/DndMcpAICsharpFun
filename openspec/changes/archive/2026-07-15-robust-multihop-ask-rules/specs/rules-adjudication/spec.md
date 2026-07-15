## MODIFIED Requirements

### Requirement: Grounded, cited rules-adjudication tool

The system SHALL expose a per-session chat tool `ask_rules(question, ruleTopics?, edition?)` that
retrieves rule passages scoped to the core rulebooks and returns them each with its citation (source
book and section or title). The tool SHALL NOT be ownership-gated and SHALL NOT accept a user or
campaign id as an argument (rules are universal). Its contract SHALL require the answer to be composed
only from the returned cited passages: naming the rules combined and citing each, flagging where the
rules do not explicitly resolve an interaction (rules-as-written versus a DM ruling), and stating that
the rules do not directly cover the question when no relevant passages are returned — never inventing a
rule. When no `edition` is supplied, retrieval SHALL NOT be restricted by edition.

The tool SHALL support multi-hop retrieval via an optional `ruleTopics` list: when the caller supplies
the distinct rules a question involves, the system SHALL run one scoped retrieval per topic (each
scoped to the core rulebooks) so that every named rule is grounded independently of the others'
ranking, and SHALL return both a per-topic grouping of the retrieved passages and a de-duplicated
combined passage list. In multi-hop mode the system SHALL ALSO run one single-shot retrieval on the
whole question (scoped to the core rulebooks) and include its passages in the de-duplicated combined
list, as a deterministic safety net so that a rule the caller omitted from `ruleTopics` can still
surface; this whole-question retrieval SHALL NOT alter the per-topic grouping. When `ruleTopics` is
absent or empty, the system SHALL perform a single-shot retrieval on the question (the default,
unchanged behaviour) and the per-topic grouping SHALL be empty.

#### Scenario: Rules question returns cited rulebook passages

- **WHEN** `ask_rules` is called with a rules question that the rulebooks cover
- **THEN** it SHALL return passages drawn only from the core rulebooks, each carrying its citation

#### Scenario: Ruling composed only from retrieved rules

- **WHEN** the tool returns rule passages for a question
- **THEN** the contract SHALL require the ruling to be composed from those passages, name the rules combined, and flag a rules-as-written versus DM-ruling distinction where the interaction is not explicitly resolved

#### Scenario: Honest empty when the rules do not cover it

- **WHEN** the scoped retrieval returns no relevant rule passages
- **THEN** the tool SHALL return an explicit empty result and the contract SHALL require stating the rules do not directly cover the question, not an invented rule

#### Scenario: Tool schema does not expose a user or campaign id

- **WHEN** the `ask_rules` tool schema is inspected
- **THEN** it SHALL NOT expose a `userId` or `campaignId` argument

#### Scenario: Multi-hop retrieves each named rule separately

- **WHEN** `ask_rules` is called with `ruleTopics` naming more than one rule (e.g. grappling and prone)
- **THEN** the system SHALL run one scoped retrieval per topic and return a per-topic grouping of the retrieved passages, so each named rule is grounded regardless of the others' ranking

#### Scenario: Multi-hop passages are combined and de-duplicated

- **WHEN** multi-hop retrieval returns overlapping passages across topics
- **THEN** the combined passage list SHALL be de-duplicated while the per-topic grouping SHALL retain each passage under every topic it was retrieved for

#### Scenario: Multi-hop includes a whole-question safety-net retrieval

- **WHEN** `ask_rules` is called with a non-empty `ruleTopics` list
- **THEN** the system SHALL additionally run a single-shot retrieval on the whole question and include its passages in the de-duplicated combined list, so a passage relevant to the question but not returned by any named topic still appears in the combined list, while the per-topic grouping continues to hold only each topic's own passages

#### Scenario: Absent topics fall back to single-shot

- **WHEN** `ask_rules` is called with no `ruleTopics`
- **THEN** the system SHALL perform the single-shot retrieval on the question and the per-topic grouping SHALL be empty
