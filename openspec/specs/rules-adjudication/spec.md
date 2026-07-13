# rules-adjudication Specification

## Purpose
TBD - created by archiving change rules-adjudication. Update Purpose after archive.
## Requirements
### Requirement: Rule retrieval is scoped to the core rulebooks

The system SHALL provide a fixed set of rule source books (the core rulebooks) whose values equal the
real `dnd_blocks` source-book payload, and SHALL scope rules retrieval to that set using the
source-book filter so that non-rule prose (e.g. monster stat blocks) is excluded from the retrieved
passages. Retrieval SHALL request enough passages that a question spanning more than one rule can
surface each of the relevant rules.

#### Scenario: Rules retrieval excludes monster prose

- **WHEN** a rules question is retrieved for adjudication
- **THEN** the returned passages SHALL come only from the core rulebook set and SHALL exclude blocks from other books (e.g. the Monster Manual)

#### Scenario: Multi-rule questions retrieve enough breadth

- **WHEN** a question spans more than one rule (e.g. grappling while prone)
- **THEN** retrieval SHALL request enough passages that each of the relevant rules can appear in the result set

### Requirement: Grounded, cited rules-adjudication tool

The system SHALL expose a per-session chat tool `ask_rules(question, edition?)` that retrieves rule
passages scoped to the core rulebooks and returns them each with its citation (source book and section
or title). The tool SHALL NOT be ownership-gated and SHALL NOT accept a user or campaign id as an
argument (rules are universal). Its contract SHALL require the answer to be composed only from the
returned cited passages: naming the rules combined and citing each, flagging where the rules do not
explicitly resolve an interaction (rules-as-written versus a DM ruling), and stating that the rules do
not directly cover the question when no relevant passages are returned — never inventing a rule. When
no `edition` is supplied, retrieval SHALL NOT be restricted by edition.

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

