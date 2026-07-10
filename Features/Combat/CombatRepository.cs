using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

namespace DndMcpAICsharpFun.Features.Combat;

/// <summary>
/// Ownership-scoped persistence for combats and their combatants. Every read and command is scoped
/// by <c>CampaignId</c> and <c>UserId</c> (combat-targeted commands also by the combat <c>Id</c>),
/// so one user can never see or mutate another's combat, even within a shared campaign. Uses
/// short-lived contexts from <see cref="IDbContextFactory{AppDbContext}"/> (Blazor-safe).
/// </summary>
public sealed class CombatRepository(IDbContextFactory<AppDbContext> dbf)
{
    public async Task<long?> StartAsync(long userId, long campaignId, string name, DndVersion edition)
    {
        await using var db = await dbf.CreateDbContextAsync();
        var hasActive = await db.Combats
            .AnyAsync(c => c.CampaignId == campaignId && c.UserId == userId && c.Status == CombatStatus.Active);
        if (hasActive) return null;

        var combat = new Combat
        {
            CampaignId = campaignId,
            UserId = userId,
            Name = name,
            Edition = edition,
            Status = CombatStatus.Active,
            Round = 1,
            CurrentTurnIndex = 0,
            CreatedAt = DateTime.UtcNow,
        };
        db.Combats.Add(combat);
        await db.SaveChangesAsync();
        return combat.Id;
    }

    public async Task<Combat?> GetActiveAsync(long campaignId, long userId)
    {
        await using var db = await dbf.CreateDbContextAsync();
        return await db.Combats.AsNoTracking()
            .Where(c => c.CampaignId == campaignId && c.UserId == userId && c.Status == CombatStatus.Active)
            .OrderByDescending(c => c.Id)
            .FirstOrDefaultAsync();
    }

    public async Task<Combat?> GetByIdAsync(long combatId, long campaignId, long userId)
    {
        await using var db = await dbf.CreateDbContextAsync();
        return await db.Combats.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == combatId && c.CampaignId == campaignId && c.UserId == userId);
    }

    public async Task<IReadOnlyList<Combatant>> GetCombatantsAsync(long combatId, long campaignId, long userId)
    {
        await using var db = await dbf.CreateDbContextAsync();
        var owns = await db.Combats
            .AnyAsync(c => c.Id == combatId && c.CampaignId == campaignId && c.UserId == userId);
        if (!owns) return [];
        return await db.Combatants.AsNoTracking()
            .Where(x => x.CombatId == combatId)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<Combat>> GetHistoryAsync(long campaignId, long userId)
    {
        await using var db = await dbf.CreateDbContextAsync();
        return await db.Combats.AsNoTracking()
            .Where(c => c.CampaignId == campaignId && c.UserId == userId && c.Status == CombatStatus.Ended)
            .OrderByDescending(c => c.EndedAt)
            .ThenByDescending(c => c.Id)
            .ToListAsync();
    }

    public async Task EndAsync(long combatId, long campaignId, long userId)
    {
        await using var db = await dbf.CreateDbContextAsync();
        await db.Combats
            .Where(c => c.Id == combatId && c.CampaignId == campaignId && c.UserId == userId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Status, CombatStatus.Ended)
                .SetProperty(c => c.EndedAt, DateTime.UtcNow));
    }
}
