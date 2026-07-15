## ADDED Requirements

### Requirement: The chat invokes the model in native reasoning (think-on) mode

`DndChatService` SHALL build the per-request `ChatOptions` WITHOUT any `RawRepresentationFactory` that
forces the model's `think` field off — i.e. it MUST NOT emit a request that sets `Think = false`, so the
local qwen3 model reasons in its default think-on mode for the conversational chat. Tool selection and
final composition therefore both run with reasoning enabled. (Rationale: think-off produces incorrect
multi-rule rulings while tool selection/binding is unchanged between modes; this is code behavior, so a
test asserts the mode rather than the model's runtime output.)

#### Scenario: Chat request does not force think-off

- **WHEN** `DndChatService` builds the `ChatOptions` for a chat request
- **THEN** the options SHALL NOT carry a `RawRepresentationFactory` that yields an OllamaSharp
  `ChatRequest` with `Think = false`

#### Scenario: Tool set is unchanged by the mode switch

- **WHEN** `DndChatService` builds the `ChatOptions` for a chat request
- **THEN** the options SHALL still expose the same chat tool set (the mode change affects only reasoning,
  not which tools are offered)

### Requirement: The model's reasoning trace is never surfaced to the user

The reply produced by the chat SHALL contain only the model's answer content, never its internal
reasoning trace. Under think-on, the reasoning is returned by Ollama in a field distinct from the answer
content (`message.thinking` vs `message.content`) and MUST NOT appear in the persisted or displayed
assistant turn.

#### Scenario: Assistant reply excludes the reasoning trace

- **WHEN** the chat composes an answer with reasoning enabled
- **THEN** the assistant text added to history, persisted, and shown to the user SHALL be the answer
  content only, with no reasoning-trace markup (e.g. no `<think>…</think>` block)
