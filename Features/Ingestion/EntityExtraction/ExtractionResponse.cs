using System.Text.Json;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

public sealed record ExtractionResponse(
    bool Success,
    JsonElement? ToolInput,      // present when Success
    string? StopReason,          // "tool_use", "end_turn", "max_tokens", etc.
    int InputTokens,
    int OutputTokens,
    string? ErrorMessage,
    string? RawJson);            // for debugging