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

        var books = SettingCatalog.Resolve(campaign.Setting);
        var query = new RetrievalQuery(
            question, Version: version,
            SourceBooks: books.Count > 0 ? books.ToArray() : null);

        var results = await rag.SearchAsync(query, ct);

        var passages = results.Select(r => new CitedPassage(
            r.Text,
            r.Metadata.SourceBook,
            r.Metadata.SectionTitle ?? r.Metadata.Chapter,
            r.Score)).ToList();

        return new SettingLoreResult(campaign.Setting, books.ToList(), passages);
    }
}
