namespace DndMcpAICsharpFun.Domain;

public sealed record Campaign(long Id, long UserId, string Name, string Description, DateTime CreatedAt, string? Setting = null);

public sealed record CampaignSummary(long Id, long UserId, string Name, string Description, DateTime CreatedAt, int HeroCount);