using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Lore;      // CitedPassage
using DndMcpAICsharpFun.Features.Retrieval;
using DndMcpAICsharpFun.Features.Retrieval.Entities;

namespace DndMcpAICsharpFun.Features.Rules;

/// <summary>
/// Retrieves rule prose for a universal (non-campaign, non-user) rules question, scoped to the core
/// rulebooks so monster/lore prose is excluded, and returns cited passages for the chat persona to
/// frame into a ruling. Not ownership-gated. Does NOT call an LLM.
/// </summary>
public sealed class RulesAdjudicationService(IRagRetrievalService rag, IEntityRetrievalService? entities = null)
{
    public async Task<RulesRulingResult> AskAsync(
        string question, IReadOnlyList<string>? ruleTopics, DndVersion? edition, CancellationToken ct)
    {
        var ruling = await AskProseAsync(question, ruleTopics, edition, ct);
        return await BestEntityAsync(question, ct) is { } entity
            ? ruling with { Passages = [entity, .. ruling.Passages] }
            : ruling;
    }

    /// <summary>
    /// The best-matching core-rulebook entity, as a passage: prose blocks lose tables and headings (the Travel Pace
    /// table, a class table, "Quickened Spell"), the entity's text holds them.
    /// </summary>
    private async Task<CitedPassage?> BestEntityAsync(string question, CancellationToken ct)
    {
        if (entities is null) return null;
        var hits = await entities.SearchAsync(new EntitySearchQuery(
            question, null, null, null, null, null, null, null, null, null, null, TopK: 3), ct);
        // Only the top hit carries its wide text, so a lower core-rulebook hit is no better than the prose.
        return hits.FirstOrDefault() is { } top && RuleSources.Keys.Contains(top.SourceBook)
            ? new CitedPassage(top.Snippet, BookCatalog.ToDisplayNames([top.SourceBook]).Single(), top.Name, top.Score,
                BookCatalog.PrintedPage(top.SourceBook, top.Page))
            : null;
    }

    private async Task<RulesRulingResult> AskProseAsync(
        string question, IReadOnlyList<string>? ruleTopics, DndVersion? edition, CancellationToken ct)
    {
        // Single-shot (v1) when the caller didn't decompose the question.
        if (ruleTopics is not { Count: > 0 })
        {
            var single = await RetrieveAsync(question, edition, RuleSources.TopK, ct);
            return new RulesRulingResult(single, DndMcpAICsharpFun.Features.Retrieval.BookCatalog.ToDisplayNames(RuleSources.Keys), []);
        }

        // Multi-hop: ground each named rule with its own scoped retrieval.
        var topicGroups = new List<RuleTopicPassages>(ruleTopics.Count);
        foreach (var topic in ruleTopics)
        {
            var topicPassages = await RetrieveAsync(topic, edition, RuleSources.TopicTopK, ct);
            topicGroups.Add(new RuleTopicPassages(topic, topicPassages));
        }

        // Deterministic safety net: `ruleTopics` decomposition is only ~80% reliable (the model can
        // drop a topic), so also retrieve on the whole question and fold it into the combined list
        // (not into topicGroups) so a dropped topic's rule can still surface.
        var whole = await RetrieveAsync(question, edition, RuleSources.TopK, ct);

        // Flat union de-duped by citation identity, keeping the highest-scoring copy; the per-topic
        // groups above still retain each passage under every rule it grounded.
        var merged = topicGroups
            .SelectMany(g => g.Passages)
            .Concat(whole)
            .GroupBy(p => (p.Text, p.SourceBook, p.Section))
            .Select(grp => grp.OrderByDescending(p => p.Score).First())
            .ToList();

        return new RulesRulingResult(merged, DndMcpAICsharpFun.Features.Retrieval.BookCatalog.ToDisplayNames(RuleSources.Keys), topicGroups);
    }

    private async Task<IReadOnlyList<CitedPassage>> RetrieveAsync(
        string query, DndVersion? edition, int topK, CancellationToken ct)
    {
        var q = new RetrievalQuery(query, Version: edition, TopK: topK, SourceKeys: RuleSources.Keys);
        var results = await rag.SearchAsync(q, ct);
        return results.Select(CitedPassage.From).ToList();
    }
}
