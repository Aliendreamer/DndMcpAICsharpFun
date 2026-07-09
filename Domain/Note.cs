namespace DndMcpAICsharpFun.Domain;

public sealed class Note
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public long CampaignId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}