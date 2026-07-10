using System.Text.Json;

using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Dice;
using DndMcpAICsharpFun.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

namespace DndMcpAICsharpFun.Features.Campaigns;

/// <summary>
/// Ownership-scoped log of dice rolls and encounter resolutions for a campaign. Every read/mutation
/// beyond insert is scoped by both <c>CampaignId</c> and <c>UserId</c> so one user can never see or
/// alter another user's log entries, even within the same campaign.
/// </summary>
public sealed class CampaignLogRepository(IDbContextFactory<AppDbContext> dbf)
{
    public async Task<long> AddRollAsync(long userId, long campaignId, RollResult roll, string? label, bool hidden = false)
    {
        var payload = new RollLogPayload(
            FormatExpression(roll.Expression),
            roll.Breakdown,
            roll.Total,
            roll.Dice,
            roll.Kept,
            roll.Expression.Mode.ToString());

        return await AddAsync(userId, campaignId, CampaignLogKind.Roll, JsonSerializer.Serialize(payload), label, hidden);
    }

    public async Task<long> AddEncounterAsync(long userId, long campaignId, EncounterLogPayload payload, string? label, bool hidden)
    {
        return await AddAsync(userId, campaignId, CampaignLogKind.Encounter, JsonSerializer.Serialize(payload), label, hidden);
    }

    public async Task<long> AddCombatAsync(long userId, long campaignId, CombatLogPayload payload, string? label)
    {
        return await AddAsync(userId, campaignId, CampaignLogKind.Combat, JsonSerializer.Serialize(payload), label, hidden: false);
    }

    public async Task<List<CampaignLogEntry>> GetByCampaignAsync(long campaignId, long userId)
    {
        await using var db = await dbf.CreateDbContextAsync();
        return await db.CampaignLogEntries.AsNoTracking()
            .Where(x => x.CampaignId == campaignId && x.UserId == userId)
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .ToListAsync();
    }

    public async Task RevealAsync(long id, long campaignId, long userId)
    {
        await using var db = await dbf.CreateDbContextAsync();
        await db.CampaignLogEntries
            .Where(x => x.Id == id && x.CampaignId == campaignId && x.UserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Hidden, false));
    }

    public async Task DeleteAsync(long id, long campaignId, long userId)
    {
        await using var db = await dbf.CreateDbContextAsync();
        await db.CampaignLogEntries
            .Where(x => x.Id == id && x.CampaignId == campaignId && x.UserId == userId)
            .ExecuteDeleteAsync();
    }

    private async Task<long> AddAsync(long userId, long campaignId, CampaignLogKind kind, string payloadJson, string? label, bool hidden)
    {
        await using var db = await dbf.CreateDbContextAsync();
        var entry = new CampaignLogEntry
        {
            CampaignId = campaignId,
            UserId = userId,
            Kind = kind,
            Label = label,
            Hidden = hidden,
            CreatedAt = DateTime.UtcNow,
            PayloadJson = payloadJson,
        };
        db.CampaignLogEntries.Add(entry);
        await db.SaveChangesAsync();
        return entry.Id;
    }

    private static string FormatExpression(DiceExpression e)
    {
        var prefix = e.Count == 1 ? $"d{e.Die}" : $"{e.Count}d{e.Die}";
        var modifier = e.Modifier switch
        {
            0 => string.Empty,
            > 0 => $"+{e.Modifier}",
            _ => e.Modifier.ToString(),
        };
        return prefix + modifier;
    }
}