namespace DndMcpAICompanion.Features.Chat;

public sealed class RateLimitOptions
{
    public int MessagesPerMinute { get; init; } = 10;
}
