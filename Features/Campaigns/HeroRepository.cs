using DndMcpAICsharpFun.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

using DndMcpAICsharpFun.Domain;

namespace DndMcpAICsharpFun.Features.Campaigns;






public sealed class HeroRepository(IDbContextFactory<AppDbContext> dbf)
{
    public async Task<List<Hero>> GetByCampaignAsync(long campaignId)
    {
        await using var db = await dbf.CreateDbContextAsync();
        var heroes = await db.Heroes.AsNoTracking()
            .Where(h => h.CampaignId == campaignId)
            .OrderBy(h => h.CreatedAt)
            .ToListAsync();
        foreach (var h in heroes)
            h.LatestSnapshot = await LatestSnapshotAsync(db, h.Id);
        return heroes;
    }

    public async Task<List<HeroWithCampaign>> GetAllByUserAsync(long userId)
    {
        await using var db = await dbf.CreateDbContextAsync();
        var rows = await (from h in db.Heroes.AsNoTracking()
                          join c in db.Campaigns.AsNoTracking() on h.CampaignId equals c.Id
                          where c.UserId == userId
                          orderby c.Name, h.Name
                          select new { Hero = h, CampaignName = c.Name }).ToListAsync();
        var results = new List<HeroWithCampaign>(rows.Count);
        foreach (var row in rows)
        {
            row.Hero.LatestSnapshot = await LatestSnapshotAsync(db, row.Hero.Id);
            results.Add(new HeroWithCampaign(row.Hero, row.CampaignName));
        }
        return results;
    }

    public async Task<Hero?> GetByIdAsync(long id)
    {
        await using var db = await dbf.CreateDbContextAsync();
        var hero = await db.Heroes.AsNoTracking().FirstOrDefaultAsync(h => h.Id == id);
        if (hero is null) return null;
        hero.LatestSnapshot = await LatestSnapshotAsync(db, hero.Id);
        return hero;
    }

    public async Task<List<HeroSnapshotMeta>> GetSnapshotsAsync(long heroId)
    {
        await using var db = await dbf.CreateDbContextAsync();
        return await db.HeroSnapshots.AsNoTracking()
            .Where(s => s.HeroId == heroId)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new HeroSnapshotMeta(s.Id, s.HeroId, s.SessionNumber, s.SessionLabel, s.Level, s.CreatedAt))
            .ToListAsync();
    }

    public async Task<HeroSnapshot?> GetSnapshotAsync(long snapshotId)
    {
        await using var db = await dbf.CreateDbContextAsync();
        return await db.HeroSnapshots.AsNoTracking().FirstOrDefaultAsync(s => s.Id == snapshotId);
    }

    public async Task<long> CreateAsync(long campaignId, string name)
    {
        await using var db = await dbf.CreateDbContextAsync();
        var hero = new Hero(0, campaignId, name, DateTime.UtcNow);
        db.Heroes.Add(hero);
        await db.SaveChangesAsync();
        db.HeroSnapshots.Add(new HeroSnapshot(0, hero.Id, 0, "Created", 0, DateTime.UtcNow, new CharacterSheet()));
        await db.SaveChangesAsync();
        return hero.Id;
    }

    public async Task SaveSnapshotAsync(long heroId, int sessionNumber, string sessionLabel, CharacterSheet sheet)
    {
        await using var db = await dbf.CreateDbContextAsync();
        db.HeroSnapshots.Add(new HeroSnapshot(0, heroId, sessionNumber, sessionLabel, sheet.Level, DateTime.UtcNow, sheet));
        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(long id)
    {
        await using var db = await dbf.CreateDbContextAsync();
        await db.HeroSnapshots.Where(s => s.HeroId == id).ExecuteDeleteAsync();
        await db.Heroes.Where(h => h.Id == id).ExecuteDeleteAsync();
    }

    private static Task<HeroSnapshot?> LatestSnapshotAsync(AppDbContext db, long heroId) =>
        db.HeroSnapshots.AsNoTracking()
            .Where(s => s.HeroId == heroId)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync();
}
