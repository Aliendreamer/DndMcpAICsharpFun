namespace DndMcpAICsharpFun.Domain;

public sealed class ChatTurn
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public long? CampaignId { get; set; }
    public long? HeroId { get; set; }
    public string Role { get; set; } = string.Empty;   // "user" | "assistant"
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
