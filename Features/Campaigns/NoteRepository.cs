using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

namespace DndMcpAICsharpFun.Features.Campaigns;

/// <summary>Free-form notes scoped to a campaign.</summary>
public sealed class NoteRepository(IDbContextFactory<AppDbContext> dbf)
{
    public async Task<List<Note>> GetByCampaignAsync(long campaignId)
    {
        await using var db = await dbf.CreateDbContextAsync();
        return await db.Notes.AsNoTracking()
            .Where(n => n.CampaignId == campaignId)
            .OrderByDescending(n => n.UpdatedAt)
            .ThenByDescending(n => n.Id)
            .ToListAsync();
    }

    public async Task<long> CreateAsync(long userId, long campaignId, string title, string content)
    {
        await using var db = await dbf.CreateDbContextAsync();
        var now = DateTime.UtcNow;
        var note = new Note
        {
            UserId = userId,
            CampaignId = campaignId,
            Title = title,
            Content = content,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Notes.Add(note);
        await db.SaveChangesAsync();
        return note.Id;
    }

    public async Task UpdateAsync(long id, long campaignId, string title, string content)
    {
        await using var db = await dbf.CreateDbContextAsync();
        var note = await db.Notes.FirstOrDefaultAsync(n => n.Id == id && n.CampaignId == campaignId);
        if (note is null) return;
        note.Title = title;
        note.Content = content;
        note.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(long id, long campaignId)
    {
        await using var db = await dbf.CreateDbContextAsync();
        await db.Notes.Where(n => n.Id == id && n.CampaignId == campaignId).ExecuteDeleteAsync();
    }
}