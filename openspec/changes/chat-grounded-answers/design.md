## Context

See the proposal. Prompt budget: `num_ctx` 8192; persona plus tools take about 2,800 tokens, and the model thinks before answering.

## Goals / Non-Goals

Goals: the fact reaches the model, and no game question is answered from memory. Non-goals: book names in results, the lost minus sign, the eval client's 180 s timeout, `search_dnd` ranking.

## Decisions

- **Text budget.** A result named in the query comes first (longest name first; Rule and Lore only when the query is exactly their name, because questions hold words like "attack"). The first three results return wide text: 3,000 characters when named, else 1,500. Longer text is cut to a window around the reranker's best piece (`EntityTextPieces.Window`). Every other result keeps 240 characters. A comparison (two calls) stays under about 2,700 tokens in the usual case.
- **Named first inside the candidate pool**, not through a separate name index: the pool already holds the named entity, only its place was wrong (Tarrasque third behind "Statistics for Objects").
- **The chat searches after a tool-less answer**, rather than before every turn: turns that already call a tool stay as they are. The ungrounded first answer is dropped, not shown to the second call.
- **Two lookups, through the offered tool objects**: `search_entities` and `ask_rules` (when offered) on the question. They are added as tool-call and tool-result messages, so the model sees them as its own and the trace records them.
- **Only for questions**: the message holds `?` or opens with an asking word. Greetings and thanks cost no second call.
- A follow-up of fewer than six words is searched together with the previous user message.
- The eval counts an app-made search as a called tool; the facts must still be in its result and in the answer.

## Risks / Trade-offs

- A turn without a tool call takes a second model call (about 20 s).
- A general question that needs no search ("what did I just ask?") is searched too; the history stays in the second call.
