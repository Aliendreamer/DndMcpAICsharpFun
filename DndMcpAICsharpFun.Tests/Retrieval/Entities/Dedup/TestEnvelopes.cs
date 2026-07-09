using System.Text.Json;

using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Tests.Retrieval.Entities.Dedup;

internal static class TestEnvelopes
{
    private static readonly JsonElement EmptyFields = JsonDocument.Parse("{}").RootElement.Clone();

    public static EntityEnvelope Make(
        string id, string name, EntityType type, string edition,
        string sourceBook = "PHB", string dataSource = "", bool needsReview = false,
        string canonicalText = "text") =>
        new(
            Id: id, Type: type, Name: name, SourceBook: sourceBook, Edition: edition,
            Page: null, FirstAppearedIn: new FirstAppearance(sourceBook, edition),
            RevisedIn: [], SettingTags: [], CanonicalText: canonicalText, Fields: EmptyFields,
            DataSource: dataSource, NeedsReview: needsReview);
}