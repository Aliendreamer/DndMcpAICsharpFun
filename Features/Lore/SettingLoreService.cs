using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Campaigns;
using DndMcpAICsharpFun.Features.Retrieval;

namespace DndMcpAICsharpFun.Features.Lore;

/// <summary>
/// Answers a lore question for one of the caller's OWN campaigns, scoped to that campaign's setting
/// source books. Ownership is enforced here (SEC-08, mirrors EncounterDesignService): a campaign that
/// is not the caller's throws and never reaches retrieval. Returns cited passages for the chat persona
/// to synthesize from — it does NOT itself call an LLM.
/// </summary>
public sealed class SettingLoreService(CampaignRepository campaigns, IRagRetrievalService rag)
{
    public async Task<SettingLoreResult> AskForUserAsync(
        long userId, long campaignId, string question, DndVersion? version, CancellationToken ct)
    {
        var campaign = await campaigns.GetByIdAsync(campaignId, userId)
            ?? throw new UnauthorizedAccessException("Campaign not found or not owned by the caller.");

        return await SearchAsync(campaign.Setting, question, version, ct);
    }

    /// <summary>Answers from a named setting's books, or from every book when the setting is blank or
    /// unknown. Reads no campaign.</summary>
    public Task<SettingLoreResult> AskAsync(string? setting, string question, CancellationToken ct) =>
        SearchAsync(setting, question, version: null, ct);

    private async Task<SettingLoreResult> SearchAsync(
        string? setting, string question, DndVersion? version, CancellationToken ct)
    {
        var books = SettingCatalog.Resolve(setting);
        var query = new RetrievalQuery(
            question, Version: version,
            SourceKeys: books.Count > 0 ? books.ToArray() : null);

        var results = await rag.SearchAsync(query, ct);

        var passages = results.Select(CitedPassage.From).ToList();

        return new SettingLoreResult(setting, DndMcpAICsharpFun.Features.Retrieval.BookCatalog.ToDisplayNames(books), passages);
    }
}
