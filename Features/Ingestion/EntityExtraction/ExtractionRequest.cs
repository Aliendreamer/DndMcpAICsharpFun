using System.Text.Json;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

public sealed record ExtractionRequest(
    string SystemPrompt,
    string UserPrompt,
    string ToolName,             // e.g. "emit_class_fields"
    string ToolDescription,      // human-readable
    JsonElement ToolInputSchema, // the per-type JSON schema
    string ModelId,
    int MaxOutputTokens);
