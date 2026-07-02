namespace DndMcpAICsharpFun.Domain;

public sealed record Hero(long Id, long CampaignId, string Name, DateTime CreatedAt)
{
    /// <summary>Latest snapshot, populated by the repository as a projection (not a mapped column).</summary>
    public HeroSnapshot? LatestSnapshot { get; set; }
}

public sealed record HeroSnapshot(long Id, long HeroId, int SessionNumber, string SessionLabel, int Level, DateTime CreatedAt, CharacterSheet Sheet);

public sealed record HeroSnapshotMeta(long Id, long HeroId, int SessionNumber, string SessionLabel, int Level, DateTime CreatedAt);

public sealed record HeroWithCampaign(Hero Hero, string CampaignName);
