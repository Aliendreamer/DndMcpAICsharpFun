## Context

`DndChatService` (Microsoft.Extensions.AI) holds `History: List<ChatMessage>` and, in `SendAsync`, appends the user message, calls `chatClient.GetResponseAsync(History, ChatOptions { Tools })`, then appends + persists the assistant reply. There is no `ChatRole.System` message. `PersistAsync(role, content)` writes `ChatTurn` rows; `ClearAsync` clears `History` and deletes persisted turns. Tools come from `IMcpToolsProvider`. Config lives in `Config/appsettings.json`.

## Goals / Non-Goals

**Goals:**
- A system-prompt persona drives voice + response style + grounding behavior.
- Persona is swappable via config without code changes; safe default always available.
- Conversation persistence/replay is unchanged (system prompt is request-only).

**Non-Goals:**
- Retrieval/tool changes (spec #3).
- A persona-editor UI.
- Per-user personas (single app-level persona in v1).

## Decisions

**1. Request-time injection, not history.** Build the per-request message list as `[new ChatMessage(ChatRole.System, personaText), ...History]` and pass that to `GetResponseAsync`. Do not add the system message to `History` and do not `PersistAsync` it. This keeps persistence/replay (which reload `History` from `ChatTurn` rows) clean and lets the persona change between requests/deploys without rewriting stored turns.

**2. `PersonaProvider`.** Resolves the active persona text: read `Chat:Persona` (default `"companion"`), load `Config/personas/<persona>.md`, cache the text. If the file is absent or empty, fall back to a built-in default constant (so chat never runs prompt-less). The provider is a singleton initialized at startup; the active text is read per request (cheap, cached). Hot-swap requires a restart (acceptable).

**3. Default `companion` persona file.** `Config/personas/companion.md` encodes the agreed directives:
- Identity/voice: a warm, knowledgeable D&D 5e companion at the table — practical, encouraging, personable; not a role-played character.
- Response style: conversational prose by default; use lists only when the user asks to compare/enumerate or it materially aids clarity; never reflexively emit "Option 1/2/3".
- Grounding: use the retrieval tools to ground rules/lore in the indexed books, preferring `search_dnd`; weave retrieved facts in naturally and cite source book/page when relevant; if retrieval returns nothing, say so and give best general guidance rather than inventing rules.
- Scope: a D&D 5e helper; concise but not terse.

**4. Tool steering is advisory.** The prompt names `search_dnd` as preferred but the model still has `search_lore`/`search_entities`/`get_entity`/`search_web`; if `search_dnd` is absent (spec #3 not yet applied) the guidance degrades gracefully.

**5. Config shape.** Add a `Chat` options section (`Chat:Persona`, `Chat:PersonasDirectory` default `Config/personas`). Bind via options; `PersonaProvider` consumes it. The personas directory ships with the app (baked into the image like the rest of `Config/`).

## Risks / Trade-offs

- **Prompt is a content artifact, not testable for quality.** We test wiring (one system message, equal to active persona, not persisted) and provider resolution/fallback — not whether responses "feel" conversational. Accepted; the prompt text is iterated by hand.
- **System prompt + small local model (qwen3:8b).** A long prompt may be partially ignored by a small model. Mitigation: keep the default persona tight and directive; it is a file, easy to tune.
- **Single app-level persona.** No per-user override in v1. Accepted; the file/config mechanism is the extension point.
- **Baked personas dir.** Editing a persona requires a redeploy/restart (Config is baked into the image). Consistent with how the app already ships `Config/`; acceptable.
