namespace DndMcpAICsharpFun.Domain;

public enum CampaignLogKind
{
    Roll,
    Encounter,
}

public sealed class CampaignLogEntry
{
    public long Id { get; set; }
    public long CampaignId { get; set; }
    public long UserId { get; set; }
    public CampaignLogKind Kind { get; set; }
    public string? Label { get; set; }
    public bool Hidden { get; set; }
    public DateTime CreatedAt { get; set; }
    public string PayloadJson { get; set; } = "";
}