## ADDED Requirements

### Requirement: The persona MUST direct correct labeling of tool-result numbers

The companion persona SHALL instruct the model to report each number returned by a tool by its true
field meaning — materials cost, workweeks, days, gold cost, XP budget — and MUST NOT relabel a value
as a different quantity (for example, calling a returned materials cost the item's "market value").
The requirement is on the persona TEXT (a verifiable directive), since the model's adherence is
probabilistic; the rig measures the resulting improvement.

#### Scenario: Persona text carries a no-relabel directive

- **WHEN** the persona file is inspected
- **THEN** it MUST contain an explicit directive to name each tool-result number by its returned
  field and never relabel it as a different quantity

### Requirement: The persona MUST give precise tool-routing cues

The companion persona SHALL make the routing choice unambiguous for the symptom-prone cases: a
question asking for a computable number routes to the matching calculator/encounter tool (never
retrieval or memory), and a rules/lore/entity question routes to the retrieval tools. The directive
MUST distinguish "compute a number" from "look something up" so the model does not answer a
computable question from retrieval.

#### Scenario: Persona text distinguishes compute-vs-lookup routing

- **WHEN** the persona file is inspected
- **THEN** it MUST state that computable-number questions go to the calculator/encounter tools and
  rules/lore/entity questions go to the retrieval tools, as an explicit either/or

### Requirement: The persona MUST enforce prose-over-lists with a concrete exemplar

The companion persona SHALL keep its hard prose rule AND include at least one concrete before/after
exemplar showing a list-shaped answer rewritten as prose, because a worked example moves the local
model more reliably than a rule alone.

#### Scenario: Persona text includes a list-to-prose exemplar

- **WHEN** the persona file is inspected
- **THEN** it MUST contain a concrete example that rewrites a numbered/bulleted answer into flowing
  prose, in addition to the existing prose directive
