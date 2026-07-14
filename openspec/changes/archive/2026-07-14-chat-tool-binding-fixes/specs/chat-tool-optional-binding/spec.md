## ADDED Requirements

### Requirement: Optional chat-tool parameters MUST be schema-optional

Every in-process chat `AIFunction` registered by `DndChatService` MUST declare each of its
optional parameters with a C# default value, so that `AIFunctionFactory` marks the parameter
as non-required in the tool's JSON schema and the model MAY omit it without triggering a
`Microsoft.Extensions.AI` binding failure. A parameter being nullable (`string?`, `double?`,
`int[]?`) MUST NOT be relied on for optionality — only a C# default value (`= null`,
`= default`) makes a parameter schema-optional.

#### Scenario: Optional parameter is absent from the schema's required set

- **WHEN** any chat tool exposes a parameter documented as optional (for example
  `build_encounter`'s `theme`, `maxCr`, `minCr`, and `campaignId`; `ask_rules`'s `ruleTopics`
  and `edition`; `generate_npc`'s `maxCr`; `prep_session`'s `difficulty`)
- **THEN** that parameter name MUST NOT appear in the tool's JSON-schema `required` array

#### Scenario: The model omits an optional parameter and the tool still binds

- **WHEN** the model emits a tool call that supplies only the required parameters and omits the
  optional ones entirely (not passing them as null)
- **THEN** `AIFunction.InvokeAsync` MUST bind successfully without throwing a
  missing-required-parameter error, and the omitted parameters MUST take their default value

#### Scenario: Reordering optional parameters does not change tool identity

- **WHEN** a tool's parameter list is reordered so required parameters precede defaulted
  optional ones (as C# requires for trailing defaults)
- **THEN** the tool's name and the set of caller-supplied parameter names in its schema MUST be
  unchanged, and no caller-supplied parameter (such as `userId`) is introduced

#### Scenario: A regression test guards the invariant across all chat tools

- **WHEN** the test suite runs
- **THEN** a data-driven test MUST assert, for every affected chat tool, that its known-optional
  parameters are excluded from the tool's JSON-schema `required` array, so a future regression
  that drops a default is caught at build/test time rather than at runtime against the model
