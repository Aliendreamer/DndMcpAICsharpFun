# Extraction Pipeline Improvements — Design

> **For agentic workers:** Use `superpowers:subagent-driven-development` or `superpowers:executing-plans` to implement this plan task-by-task.

**Goal:** Make the LLM extraction pipeline robust for large, table-heavy pages by introducing semantic chunking, deterministic JSON merging, partial JSON recovery, and few-shot examples.

**Architecture:** Split-and-merge layer around the LLM call. Each candidate's text is split into semantic chunks before extraction; each chunk gets its own focused LLM call; partial outputs are merged deterministically in code. Two orthogonal improvements layer on top: few-shot examples in the system prompt, partial JSON recovery in the LLM client.

**Tech Stack:** C# / .NET 10, `System.Text.Json`, existing `OllamaEntityExtractionClient`, `ExtractionPromptBuilder`, `EntityExtractionOrchestrator`.

---

## Problem

The current pipeline sends one `EntityCandidate` (a full section's concatenated text) to the LLM in a single call. For dense pages — trap variant tables, scroll lists, item groups — this produces 24–45 KB of JSON output, blowing past the `MaxOutputTokensPerEntity` budget. The model truncates mid-JSON and the whole candidate is marked as failed.

Root causes:
1. No pre-splitting of large sections before the LLM call.
2. No partial recovery when truncation does occur.
3. No worked examples in the prompt — the model has to infer output shape from schema description alone.

---

## New Flow

```
EntityCandidate
  → SemanticChunker          split at structural boundaries (sub-headings > tables > paragraphs)
  → [Chunk₁, Chunk₂, ...]   each chunk ≤ MaxTokensPerChunk (default 2000 tokens)
  → LLM call per chunk       same schema + few-shot example in system prompt
  → [JSON₁, JSON₂, ...]     partial JSON recovery applied on truncation before failure
  → EntityFieldMerger        scalar: first non-null wins; arrays: concat + dedup by name
  → final merged JSON
```

When a candidate is below the threshold, `SemanticChunker` returns it as a single chunk — the existing path is completely unchanged.

---

## Components

### SemanticChunker

**File:** `Features/Ingestion/EntityExtraction/SemanticChunker.cs`

Splits a candidate's text at structural boundaries in priority order:
1. Sub-headings: lines starting with `##` or `###`
2. Table separators: lines matching `/^\|[-| ]+\|$/` (split before the table)
3. Double newline (paragraph break)

Algorithm:
```
tokenCount = text.Length / 4   (characters ÷ 4 ≈ tokens; no external tokenizer)

if tokenCount ≤ maxTokensPerChunk → return [text]   (no-op fast path)

identify split points by priority order above
greedily accumulate lines into a chunk until adding the next block
  would exceed maxTokensPerChunk → emit chunk, start new one
never emit an empty chunk; carry remainder into next chunk
```

Interface:
```csharp
IList<string> Split(string text, int maxTokensPerChunk)
```

Fully stateless. No dependency on Docling or LLM. Pure string → list of strings.

---

### EntityFieldMerger

**File:** `Features/Ingestion/EntityExtraction/EntityFieldMerger.cs`

Merges an ordered list of partial `JsonElement` field objects into one final object.

Merge rules (applied field by field):

| Field kind | Rule |
|---|---|
| Scalar (string, number, bool) | First non-null wins; later chunks cannot overwrite |
| Array (`entries`, `variants`, `traits`, `actions`, `reactions`, `legendaryActions`, etc.) | Concatenate all arrays in chunk order |
| Array dedup | If items have a `"name"` property, drop later duplicates with the same name |
| Object (`speed`, `hp`, `ac`, etc.) | First non-null object wins |
| Unknown/new fields | First non-null wins (safe default) |

Edge cases:
- List of one element → returns it unchanged.
- All partials empty → returns empty object `{}`.
- Chunk₁ items always appear before Chunk₂ items in concatenated arrays.

Interface:
```csharp
JsonElement Merge(IList<JsonElement> partials)
```

No entity-type awareness — operates purely on JSON structure.

---

### PartialJsonRecoverer

**File:** `Features/Ingestion/EntityExtraction/PartialJsonRecoverer.cs`

Called inside `OllamaEntityExtractionClient` when `JsonDocument.Parse` throws on the raw LLM response.

Algorithm:
```
scan raw string forward, tracking brace/bracket depth
  ++depth on { or [
  --depth on } or ]
  record lastValidClose = current position each time depth returns to 0

if lastValidClose == -1 → return (false, "")
candidate = raw[..lastValidClose+1]
if JsonDocument.Parse(candidate) succeeds → return (true, candidate)
else → return (false, "")
```

On success: the partial result is used, a warning is logged:
`"Recovered partial JSON for {Type} '{Name}': {Recovered}/{Total} chars"`

On failure: existing failure path runs unchanged — no behaviour change for the caller.

Interface:
```csharp
bool TryRecover(string raw, out string recovered)
```

---

### Few-Shot Examples

**Files:** `Schemas/examples/<EntityType>.json` (one file per type)

One minimal worked example per entity type, injected into the system prompt by `ExtractionPromptBuilder.BuildSystemPrompt` after the existing field-format hints:

```
Example output:
{"name":"Fireball","level":3,"school":"V",...}
```

Loading: `ExtractionPromptBuilder` receives an `IExampleLoader` (or reads files directly) at construction. Examples are loaded once at startup from `Schemas/examples/`. If a file does not exist for a type, the builder skips silently — no error, no fallback text.

Priority order for example files: exact type name match (`Trap.json`, `Spell.json`, etc.).

---

## Wiring Changes

### `EntityExtractionOptions`

Two new fields:
```csharp
public int MaxTokensPerChunk { get; set; } = 2000;
public string ExamplesDirectory { get; set; } = "Schemas/examples";
```

### `appsettings.json`

Add under `EntityExtraction`:
```json
"MaxTokensPerChunk": 2000,
"ExamplesDirectory": "Schemas/examples"
```

### `EntityExtractionOrchestrator` — inner extraction loop

Replace single LLM call with chunked loop:

```csharp
var chunks = chunker.Split(candidate.Text, _opts.MaxTokensPerChunk);
var partials = new List<JsonElement>();
var chunkFailures = new List<int>();

for (int c = 0; c < chunks.Count; c++)
{
    var req = BuildRequest(candidate, schema, chunkText: chunks[c]);
    var resp = await retry.ExecuteAsync(..., ct);
    if (resp.Success && resp.ToolInput is not null)
        partials.Add(resp.ToolInput.Value);
    else
        chunkFailures.Add(c);
}

if (partials.Count == 0)
{
    // all chunks failed → record as failure (same as today)
}
else
{
    if (chunkFailures.Count > 0)
        logger.LogWarning("Partial extraction for {Id}: chunks {Failed} failed, {Ok} ok",
            id, chunkFailures, partials.Count);

    var merged = partials.Count == 1 ? partials[0] : merger.Merge(partials);
    // → build EntityEnvelope with merged fields, same as today
}
```

### `OllamaEntityExtractionClient`

In the `JsonException` catch block, before returning failure:
```csharp
if (recoverer.TryRecover(rawText, out var recovered))
{
    var doc = JsonDocument.Parse(recovered);
    return new ExtractionResponse(Success: true, ToolInput: doc.RootElement.Clone(),
        StopReason: stopReason, ..., ErrorMessage: null, RawJson: recovered);
}
// else fall through to existing failure return
```

---

## File Map

| Action | Path |
|---|---|
| Create | `Features/Ingestion/EntityExtraction/SemanticChunker.cs` |
| Create | `Features/Ingestion/EntityExtraction/EntityFieldMerger.cs` |
| Create | `Features/Ingestion/EntityExtraction/PartialJsonRecoverer.cs` |
| Create | `Schemas/examples/Trap.json` |
| Create | `Schemas/examples/Spell.json` |
| Create | `Schemas/examples/Monster.json` |
| Create | `Schemas/examples/Item.json` |
| Create | `Schemas/examples/MagicItem.json` |
| Create | `Schemas/examples/Rule.json` |
| Create | `Schemas/examples/God.json` |
| Create | `Schemas/examples/Lore.json` |
| Modify | `Features/Ingestion/EntityExtraction/EntityExtractionOptions.cs` |
| Modify | `Features/Ingestion/EntityExtraction/ExtractionPromptBuilder.cs` |
| Modify | `Features/Ingestion/EntityExtraction/OllamaEntityExtractionClient.cs` |
| Modify | `Features/Ingestion/EntityExtraction/EntityExtractionOrchestrator.cs` |
| Modify | `Config/appsettings.json` |
| Create | `Tests/.../SemanticChunkerTests.cs` |
| Create | `Tests/.../EntityFieldMergerTests.cs` |
| Create | `Tests/.../PartialJsonRecovererTests.cs` |

---

## What Does Not Change

- `EntityCandidateScanner` — candidate detection is unchanged
- `ExtractionPromptBuilder.BuildUserPrompt` — signature unchanged; chunk text replaces `candidate.Text`
- Checkpoint / resume logic — operates on candidate IDs, unaffected by chunking
- Retry policy — applied per chunk call, not per candidate
- `CanonicalJsonWriter`, `IEntityVectorStore`, ingestion tracker — no changes
- All existing API endpoints — no changes
