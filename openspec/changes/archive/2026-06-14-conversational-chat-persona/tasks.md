## 1. PersonaProvider (TDD)

- [x] 1.1 Write failing tests: resolves `companion` when `Chat:Persona` unset; loads the named file when set; falls back to the built-in default constant when the file is missing/empty; caches after first load
- [x] 1.2 Implement `PersonaProvider` (reads `Chat:Persona` + `Chat:PersonasDirectory` via options, loads `<dir>/<persona>.md`, caches, default-fallback constant); register as singleton; make tests green

## 2. Config + default persona file

- [x] 2.1 Add a `Chat` options type (`Persona` default `companion`, `PersonasDirectory` default `Config/personas`) and bind the `Chat` config section
- [x] 2.2 Create `Config/personas/companion.md` encoding the agreed directives (warm conversational companion voice; prose over reflexive option-lists; ground via retrieval tools preferring `search_dnd` + cite book/page; never fabricate rules; D&D 5e scope; concise not terse). Ensure `Config/personas/` ships with the app

## 3. Inject persona in DndChatService (TDD)

- [x] 3.1 Write failing test with a fake `IChatClient` capturing the outgoing messages: `SendAsync` sends `[System(personaText), ...History]`; the system message equals the active persona; `History` ends with only user/assistant (no system role); `PersistAsync` is called only for user + assistant
- [x] 3.2 Update `DndChatService.SendAsync` to build `[new ChatMessage(ChatRole.System, persona.Text), ..History]` and pass that to `GetResponseAsync` (inject `PersonaProvider`); do not mutate `History`/persist the persona; make tests green

## 4. Build & suite

- [x] 4.1 `dotnet build` clean (0 warnings) and `dotnet test` fully green (existing chat tests included)

## 5. Verify

- [x] 5.1 Against the running stack, send a question (e.g. "what's a good first feat for a battlemaster?") and confirm the reply is conversational prose grounded in the books with a citation, not a bare "Option 1/2/3" list; flip `Chat:Persona` to a second test persona and confirm the voice changes
