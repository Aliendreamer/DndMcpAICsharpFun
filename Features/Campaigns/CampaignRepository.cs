using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Retrieval;
using DndMcpAICsharpFun.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

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

    public async Task<long> CreateAsync(
        long userId,
        string name,
        string description,
        string? setting = null,
        DndVersion edition = DndVersion.Edition2014,
        IReadOnlyList<string>? allowedBookKeys = null)
    {
        // No allowlist supplied -> the edition's core rulebooks only; supplements are opt-in. An
        // EXPLICIT list is preserved verbatim, including an explicitly empty one.
        var books = allowedBookKeys ?? CampaignScopeResolver.CoreBooksFor(edition);
        ValidateBookKeys(books);

        await using var db = await dbf.CreateDbContextAsync();
        var campaign = new Campaign(0, userId, name, description, DateTime.UtcNow, setting, edition, books);
        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync();
        return campaign.Id;
    }

    public async Task SetBooksAsync(long id, long userId, DndVersion edition, IReadOnlyList<string> allowedBookKeys)
    {
        ValidateBookKeys(allowedBookKeys);

        await using var db = await dbf.CreateDbContextAsync();
        await db.Campaigns.Where(c => c.Id == id && c.UserId == userId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Edition, edition)
                .SetProperty(c => c.AllowedBookKeys, allowedBookKeys));
    }

    /// <summary>Rejects a key that is not in <c>BookCatalog</c>, naming it. Storing an unmatchable key
    /// would silently scope the campaign to nothing downstream — the same class of failure as the
    /// display-name mismatch that hid TCE's blocks from every key-scoped feature.</summary>
    private static void ValidateBookKeys(IReadOnlyList<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);

        var unknown = keys.Where(k => !BookCatalog.Keys.Contains(k)).ToList();
        if (unknown.Count > 0)
        {
            throw new ArgumentException(
                $"Unknown source-book key(s): {string.Join(", ", unknown)}. Allowed keys come from BookCatalog.",
                nameof(keys));
        }
    }

    public async Task SetSettingAsync(long id, long userId, string? setting)
    {
        await using var db = await dbf.CreateDbContextAsync();
        await db.Campaigns.Where(c => c.Id == id && c.UserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Setting, setting));
    }

    public async Task DeleteAsync(long id, long userId)
    {
        await using var db = await dbf.CreateDbContextAsync();
        var owned = await db.Campaigns.AnyAsync(c => c.Id == id && c.UserId == userId);
        if (!owned) return;
        // Wrap the multi-statement delete in the retrying execution strategy: the context is configured
        // with EnableRetryOnFailure, which forbids a raw user-initiated BeginTransactionAsync.
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            var heroIds = await db.Heroes.Where(h => h.CampaignId == id).Select(h => h.Id).ToListAsync();
            await db.HeroSnapshots.Where(s => heroIds.Contains(s.HeroId)).ExecuteDeleteAsync();
            await db.Heroes.Where(h => h.CampaignId == id).ExecuteDeleteAsync();
            await db.Notes.Where(n => n.CampaignId == id).ExecuteDeleteAsync();
            await db.CampaignLogEntries.Where(x => x.CampaignId == id).ExecuteDeleteAsync();
            await db.ChatTurns.Where(t => t.CampaignId == id).ExecuteDeleteAsync();
            var combatIds = await db.Combats.Where(c => c.CampaignId == id).Select(c => c.Id).ToListAsync();
            await db.Combatants.Where(x => combatIds.Contains(x.CombatId)).ExecuteDeleteAsync();
            await db.Combats.Where(c => c.CampaignId == id).ExecuteDeleteAsync();
            await db.Campaigns.Where(c => c.Id == id && c.UserId == userId).ExecuteDeleteAsync();
            await tx.CommitAsync();
        });
    }
}