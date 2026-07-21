using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Entities.Extraction;

/// <summary>
/// Dedicated coverage for <see cref="ExtractionEntityIds.RecordedEntityId"/> — the single source of
/// truth for the id under which an extracted (or declined/errored) entity is recorded. A
/// <see cref="DeterministicOutcome.ForceType"/> resolution must always slug from the FORCED type,
/// never the candidate's stale original <c>Type</c> (audit-fixes P0: Object-rescue stat-block
/// candidates were being recorded as <c>&lt;book&gt;.monster.&lt;slug&gt;</c> while the stored
/// entity's <c>Type</c> said <c>Object</c>).
/// </summary>
public sealed class ExtractionEntityIdsTests
{
    private static DndMcpAICsharpFun.Domain.IngestionRecord Record(string? fivetoolsSourceKey = null) => new()
    {
        Id = 1,
        FilePath = "/dev/null",
        FileName = "test.pdf",
        FileHash = "h",
        Version = "5e",
        DisplayName = "Test Book",
        FivetoolsSourceKey = fivetoolsSourceKey,
    };

    // Mirrors StatBlockScanner's real shape: Type stays Monster "for stable identity" even though the
    // body's Object signature (Armor Class + Hit Points, no Challenge, "<Size> object") forces
    // EntityType.Object at resolution time — this divergence is exactly what produced the bug.
    private static EntityCandidate ObjectStatBlockCandidate() => new(
        Type: EntityType.Monster,
        DisplayName: "War Ballista",
        Text: "A Large object. Armor Class 19. Hit Points 100.",
        Page: 1,
        TypePrior: new[] { EntityType.Monster, EntityType.Object });

    [Fact]
    public void Object_forced_candidate_without_5etools_match_gets_an_object_id()
    {
        var id = ExtractionEntityIds.RecordedEntityId(Record(), ObjectStatBlockCandidate(), matcher: null);

        id.Should().Be(
            EntityIdSlug.For("Test Book", EntityType.Object, "War Ballista"),
            "the id's type segment must match the FORCED type (Object), never the candidate's stale original Type (Monster)");
        id.Should().Contain(".object.").And.NotContain(".monster.");
    }

    [Fact]
    public void Object_forced_candidate_id_uses_the_FivetoolsSourceKey_book_slug()
    {
        var id = ExtractionEntityIds.RecordedEntityId(
            Record(fivetoolsSourceKey: "MM"), ObjectStatBlockCandidate(), matcher: null, isOfficial: true);

        id.Should().Be("mm14.object.war-ballista");
    }

    [Fact]
    public void ForceType_with_a_5etools_canonical_name_uses_the_forced_type_and_canonical_name()
    {
        // Existing (already-correct) behavior: a genuine 5etools match must keep taking priority for
        // the id — unaffected by the ForceType-without-canonical-name fix.
        var matcher = new EntityNameMatcher(new EntityNameIndex(TestPaths.RepoFile("5etools")));
        var candidate = new EntityCandidate(
            Type: EntityType.Spell, DisplayName: "FIREBALL",
            Text: "A bright streak flashes to a point you choose, then blossoms into flame.",
            Page: 1, TypePrior: new[] { EntityType.Spell });

        var expectedMatch = matcher.Match("FIREBALL")!.Value;
        var id = ExtractionEntityIds.RecordedEntityId(Record(), candidate, matcher);

        id.Should().Be(EntityIdSlug.For("Test Book", expectedMatch.Type, expectedMatch.Canonical));
        id.Should().Be(EntityIdSlug.For("Test Book", EntityType.Spell, "Fireball"));
    }

    [Fact]
    public void Defer_candidate_gets_its_id_from_the_candidate_type()
    {
        // No stat-block/magic-item signature and no 5etools match; Item is not a gated type, so the
        // resolver defers to the content-first union (Outcome != ForceType). Id derivation is
        // unchanged by the fix: it comes straight from candidate.Type.
        var candidate = new EntityCandidate(
            Type: EntityType.Item, DisplayName: "Travelers Pack",
            Text: "A simple leather pack for carrying gear.",
            Page: 1, TypePrior: new[] { EntityType.Item });

        var id = ExtractionEntityIds.RecordedEntityId(Record(), candidate, matcher: null);

        id.Should().Be(EntityIdSlug.For("Test Book", EntityType.Item, "Travelers Pack"));
    }
}