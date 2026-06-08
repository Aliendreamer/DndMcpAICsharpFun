using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DndMcpAICsharpFun.Features.Chat;

/// <summary>Persists and replays chat conversation turns.</summary>
public sealed class ChatRepository(IDbContextFactory<AppDbContext> dbf)
{
    public async Task AddAsync(ChatTurn turn)
    {
        await using var db = await dbf.CreateDbContextAsync();
        db.ChatTurns.Add(turn);
        await db.SaveChangesAsync();
    }

    public async Task<List<ChatTurn>> GetHistoryAsync(long userId, long? campaignId = null, long? heroId = null)
    {
        await using var db = await dbf.CreateDbContextAsync();
        return await db.ChatTurns.AsNoTracking()
            .Where(m => m.UserId == userId && m.CampaignId == campaignId && m.HeroId == heroId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();
    }

    /// <summary>Permanently deletes a user's chat turns for the given conversation scope.</summary>
    public async Task DeleteConversationAsync(long userId, long? campaignId = null, long? heroId = null)
    {
        await using var db = await dbf.CreateDbContextAsync();
        await db.ChatTurns
            .Where(m => m.UserId == userId && m.CampaignId == campaignId && m.HeroId == heroId)
            .ExecuteDeleteAsync();
    }
}
