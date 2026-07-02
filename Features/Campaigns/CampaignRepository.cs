using DndMcpAICsharpFun.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

using DndMcpAICsharpFun.Domain;

namespace DndMcpAICsharpFun.Features.Campaigns;




public sealed class CampaignRepository(IDbContextFactory<AppDbContext> dbf)
{
    public async Task<List<CampaignSummary>> GetAllAsync(long userId)
    {
        await using var db = await dbf.CreateDbContextAsync();
        return await db.Campaigns.AsNoTracking()
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new CampaignSummary(
                c.Id, c.UserId, c.Name, c.Description, c.CreatedAt,
                db.Heroes.Count(h => h.CampaignId == c.Id)))
            .ToListAsync();
    }

    public async Task<Campaign?> GetByIdAsync(long id, long userId)
    {
        await using var db = await dbf.CreateDbContextAsync();
        return await db.Campaigns.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);
    }

    public async Task<long> CreateAsync(long userId, string name, string description)
    {
        await using var db = await dbf.CreateDbContextAsync();
        var campaign = new Campaign(0, userId, name, description, DateTime.UtcNow);
        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync();
        return campaign.Id;
    }

    public async Task DeleteAsync(long id, long userId)
    {
        await using var db = await dbf.CreateDbContextAsync();
        var owned = await db.Campaigns.AnyAsync(c => c.Id == id && c.UserId == userId);
        if (!owned) return;
        await using var tx = await db.Database.BeginTransactionAsync();
        var heroIds = await db.Heroes.Where(h => h.CampaignId == id).Select(h => h.Id).ToListAsync();
        await db.HeroSnapshots.Where(s => heroIds.Contains(s.HeroId)).ExecuteDeleteAsync();
        await db.Heroes.Where(h => h.CampaignId == id).ExecuteDeleteAsync();
        await db.Notes.Where(n => n.CampaignId == id).ExecuteDeleteAsync();
        await db.Campaigns.Where(c => c.Id == id && c.UserId == userId).ExecuteDeleteAsync();
        await tx.CommitAsync();
    }
}
