namespace DndMcpAICsharpFun.Domain;

public sealed record Hero(long Id, long CampaignId, string Name, DateTime CreatedAt, HeroSnapshot? LatestSnapshot);

public sealed record HeroSnapshot(long Id, long HeroId, int SessionNumber, string SessionLabel, int Level, DateTime CreatedAt, CharacterSheet Sheet);

public sealed record HeroSnapshotMeta(long Id, long HeroId, int SessionNumber, string SessionLabel, int Level, DateTime CreatedAt);

public sealed record HeroWithCampaign(Hero Hero, string CampaignName);
