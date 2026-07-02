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
        await AttachLatestSnapshotsAsync(db, heroes);
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
        var heroes = rows.Select(r => r.Hero).ToList();
        await AttachLatestSnapshotsAsync(db, heroes);
        return rows.Select(r => new HeroWithCampaign(r.Hero, r.CampaignName)).ToList();
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

    /// <summary>Loads a snapshot only if it belongs to a campaign owned by <paramref name="userId"/>;
    /// returns null otherwise (used to enforce per-user access to character-scoped resolution).</summary>
    public async Task<HeroSnapshot?> GetSnapshotForUserAsync(long snapshotId, long userId)
    {
        await using var db = await dbf.CreateDbContextAsync();
        return await (
            from s in db.HeroSnapshots.AsNoTracking()
            join h in db.Heroes.AsNoTracking() on s.HeroId equals h.Id
            join c in db.Campaigns.AsNoTracking() on h.CampaignId equals c.Id
            where s.Id == snapshotId && c.UserId == userId
            select s).FirstOrDefaultAsync();
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
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.HeroSnapshots.Where(s => s.HeroId == id).ExecuteDeleteAsync();
        await db.Heroes.Where(h => h.Id == id).ExecuteDeleteAsync();
        await tx.CommitAsync();
    }

    private static Task<HeroSnapshot?> LatestSnapshotAsync(AppDbContext db, long heroId) =>
        db.HeroSnapshots.AsNoTracking()
            .Where(s => s.HeroId == heroId)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync();

    // Load every hero's latest snapshot in ONE query instead of one query per hero (NET-02).
    private static async Task AttachLatestSnapshotsAsync(AppDbContext db, IReadOnlyCollection<Hero> heroes)
    {
        if (heroes.Count == 0) return;
        var heroIds = heroes.Select(h => h.Id).ToList();
        var latest = await db.HeroSnapshots.AsNoTracking()
            .Where(s => heroIds.Contains(s.HeroId))
            .GroupBy(s => s.HeroId)
            .Select(g => g.OrderByDescending(s => s.CreatedAt).First())
            .ToListAsync();
        var byHero = latest.ToDictionary(s => s.HeroId);
        foreach (var h in heroes)
            h.LatestSnapshot = byHero.GetValueOrDefault(h.Id);
    }
}
