using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Infrastructure.Ollama;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

/// <summary>
/// Tier 2 grounding judge: reuses the same <see cref="IChatClient"/> (qwen3 via Ollama) that
/// <see cref="OllamaEntityExtractionClient"/> depends on, but asks a narrower question — are the
/// entity's already-emitted fields supported by the source prose? — rather than extracting fields.
/// </summary>
public sealed class QwenGroundingJudge(
    IChatClient chat,
    IOptions<OllamaOptions> ollamaOptions,
    ILogger<QwenGroundingJudge> logger) : IGroundingJudge
{
    private const string SystemPrompt =
        "You are a strict fact-checker for a Dungeons & Dragons rules database. You will be given a " +
        "JSON object of fields extracted for a game entity, and a passage of source prose the fields " +
        "were supposedly drawn from. Decide whether the fields are supported by the prose — every " +
        "value must be traceable to the prose, not invented or drawn from general D&D knowledge. " +
        "Answer with exactly one word: \"yes\" if the fields are supported, or \"no\" if any are not.";

    public async Task<bool?> AreFieldsSupportedAsync(EntityEnvelope entity, string sourceProse, CancellationToken ct)
    {
        var fieldsJson = entity.Fields.GetRawText();
        var userPrompt =
            $"Entity: {entity.Name}\n\n" +
            $"Emitted fields (JSON):\n{fieldsJson}\n\n" +
            $"Source prose:\n{sourceProse}\n\n" +
            "Are the emitted fields supported by the source prose? Answer strictly \"yes\" or \"no\"." +
            "\n\n/no_think";

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, SystemPrompt),
            new(ChatRole.User, userPrompt),
        };

        var chatOptions = new ChatOptions
        {
            ModelId = ollamaOptions.Value.ChatModel,
            Temperature = 0f,
        };

        try
        {
            var response = await chat.GetResponseAsync(messages, chatOptions, ct);
            var reply = response.Text?.Trim() ?? string.Empty;

            if (reply.StartsWith("yes", StringComparison.OrdinalIgnoreCase)) return true;
            if (reply.StartsWith("no", StringComparison.OrdinalIgnoreCase)) return false;

            // Hedged/garbage reply — the judge could not clearly decide. Treat as "unknown", not as
            // a confirmed fabrication: the cascade maps a null verdict to Uncertain.
            logger.LogWarning(
                "Tier 2 grounding judge gave an unparsable reply for entity {EntityId}: {Reply}",
                entity.Id, reply);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A transient I/O/model failure must NOT be treated as a confirmed fabrication —
            // returning null lets the cascade fall back to Uncertain instead of Ungrounded.
            logger.LogWarning(ex, "Tier 2 grounding judge call failed for entity {EntityId}", entity.Id);
            return null;
        }
    }
}
