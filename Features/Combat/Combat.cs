using DndMcpAICsharpFun.Domain;

namespace DndMcpAICsharpFun.Features.Combat;

public enum CombatStatus
{
    Active,
    Ended,
}

/// <summary>
/// A tracked fight for a campaign: the parent aggregate of a set of <see cref="Combatant"/> rows.
/// Campaign- and user-scoped; at most one <see cref="CombatStatus.Active"/> combat per campaign.
/// </summary>
public sealed class Combat
{
    public long Id { get; set; }
    public long CampaignId { get; set; }
    public long UserId { get; set; }
    public string Name { get; set; } = "";
    public DndVersion Edition { get; set; }
    public CombatStatus Status { get; set; }
    public int Round { get; set; } = 1;
    public long? CurrentTurnCombatantId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? EndedAt { get; set; }
}
