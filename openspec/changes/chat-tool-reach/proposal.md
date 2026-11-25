## Why

The chat eval of 2026-11-25 passed 10 of 34 cases (goal: 80%). Root causes, traced on live turns: the router hid tools the persona names, and Ollama drops a call to a tool that was not offered, so 18 runs had an empty reply and no crafting question could pass; chat searched prose (`search_lore`) and never `search_dnd`; the model's edition and category arguments hid every match, although all indexed books are 2014; `ask_setting_lore` failed without a campaign.

## What Changes

- The router always offers `search_dnd`, `search_entities`, `get_entity`, `calculate_crafting` and `ask_rules`.
- An empty reply is asked again with every tool; two empty replies give a message.
- Chat no longer offers `search_lore`.
- **BREAKING** (tool schema): no edition or category arguments on the chat and search tools; encounters use 2014 rules in chat. Edition filters come back with the first 2024 book (roadmap).
- `ask_setting_lore` works without a campaign.
- The persona tells the model to look up named things and never answer from memory.
- `Tools/ModelEval/run-full.sh` runs the eval with one command and prints the share against the 80% goal.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `chat-query-routing`, `mcp-retrieval-tools`, `setting-aware-lore`, `rules-adjudication`, `downtime-advisor`, `chat-tool-optional-binding`, `full-chain-eval`

## Impact

Chat service, router options, MCP tools, setting lore service, persona, ModelEval. No data change.
