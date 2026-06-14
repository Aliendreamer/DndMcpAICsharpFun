## Why

The chat companion has no system prompt. `DndChatService.SendAsync` calls the LLM with only the user/assistant `History` and the tool list — nothing establishes who the companion is, how it should sound, or how to use the retrieved rules. The result is generic, list-prone output ("Option 1 / 2 / 3 …") instead of the warm, conversational, grounded answers a D&D companion should give. We also have no way to tune the voice without changing code.

## What Changes

- Inject a **system prompt** (persona) at request time: build the outgoing message list as `[System(persona)] + History`. The system message is never added to `History` and never persisted — conversation turns stay user/assistant only.
- Make the persona **configurable**: persona prompts are text/markdown files; a config key `Chat:Persona` (default `companion`) selects one; a `PersonaProvider` loads + caches it and falls back to a built-in default if the file is missing.
- Ship a default `companion` persona that instructs: warm, knowledgeable, conversational voice; prose by default (no reflexive bulleted "options" dumps); ground rules/lore via the retrieval tools (prefer `search_dnd`) and cite book/page; never fabricate rules.

## Capabilities

### New Capabilities

- `conversational-chat-persona`: a configurable system-prompt persona injected into chat requests, with a shipped default that produces conversational, grounded responses.

### Modified Capabilities

(none — additive to the chat send path; existing chat persistence/UI behavior unchanged)

## Impact

- Code: new `PersonaProvider` (loads `Config/personas/<name>.md` by `Chat:Persona`, caches, default-fallback); `DndChatService.SendAsync` prepends one `ChatRole.System` message from the active persona to the messages passed to `GetResponseAsync`, without touching `History` or `PersistAsync`.
- Config: `Chat:Persona` key (default `companion`); shipped `Config/personas/companion.md`. Adding personas is a file drop, no code change.
- Behavior: responses become conversational and grounded; persistence, replay, rate-limiting, and the existing tools are unchanged.
- Out of scope: retrieval changes (spec #3) and any persona-editing UI.
