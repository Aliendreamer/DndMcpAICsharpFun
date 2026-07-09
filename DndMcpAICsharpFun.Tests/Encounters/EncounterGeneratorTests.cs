using DndMcpAICsharpFun.Domain;              // DndVersion
using DndMcpAICsharpFun.Features.Encounters;

using FluentAssertions;

using Xunit;

namespace DndMcpAICsharpFun.Tests.Encounters;

public sealed class EncounterGeneratorTests
{
    private static readonly IReadOnlyList<int> Party4L5 = [5, 5, 5, 5];

    private sealed class FakeMonsterSource(IReadOnlyList<MonsterRef> pool) : IEncounterMonsterSource
    {
        public DndVersion? CapturedEdition { get; private set; }
        public double? CapturedCrGte { get; private set; }
        public double? CapturedCrLte { get; private set; }
        public string? CapturedTheme { get; private set; }
        public bool? CapturedSrdOnly { get; private set; }
        public int? CapturedLimit { get; private set; }

        public Task<IReadOnlyList<MonsterRef>> FindAsync(
            DndVersion ed, double crGte, double crLte, string? theme, bool srdOnly, int limit, CancellationToken ct)
        {
            CapturedEdition = ed;
            CapturedCrGte = crGte;
            CapturedCrLte = crLte;
            CapturedTheme = theme;
            CapturedSrdOnly = srdOnly;
            CapturedLimit = limit;
            return Task.FromResult(pool);
        }
    }

    private static IReadOnlyList<MonsterRef> FiveCr3Monsters() =>
    [
        new MonsterRef("mm.monster.one", "Monster One", 3, EncounterMath.CrToXp(3)),
        new MonsterRef("mm.monster.two", "Monster Two", 3, EncounterMath.CrToXp(3)),
        new MonsterRef("mm.monster.three", "Monster Three", 3, EncounterMath.CrToXp(3)),
        new MonsterRef("mm.monster.four", "Monster Four", 3, EncounterMath.CrToXp(3)),
        new MonsterRef("mm.monster.five", "Monster Five", 3, EncounterMath.CrToXp(3))
    ];

    [Fact]
    public async Task BuildAsync_reaches_hard_for_four_L5_party_and_matches_the_assessor()
    {
        var source = new FakeMonsterSource(FiveCr3Monsters());
        var assessor = new EncounterAssessor();
        var generator = new EncounterGenerator(source, assessor);

        var result = await generator.BuildAsync(
            Party4L5, Difficulty.Hard, DndVersion.Edition2014, theme: null, crLte: null, crGte: null, CancellationToken.None);

        result.Assessment.Difficulty.Should().Be(Difficulty.Hard);
        result.FullyMatched.Should().BeTrue();
        result.Note.Should().BeNull();

        // Build == rate: re-assessing the exact monster set the generator returned must agree
        // with what the generator itself reported — build and rate can never disagree.
        var rated = assessor.Assess(Party4L5, result.Assessment.Monsters, DndVersion.Edition2014);
        rated.Difficulty.Should().Be(Difficulty.Hard);
    }

    [Fact]
    public async Task BuildAsync_forwards_theme_and_explicit_cr_bounds_to_the_source()
    {
        var source = new FakeMonsterSource(FiveCr3Monsters());
        var generator = new EncounterGenerator(source, new EncounterAssessor());

        await generator.BuildAsync(
            Party4L5, Difficulty.Trivial, DndVersion.Edition2014, theme: "Undead", crLte: 6.0, crGte: 2.0, CancellationToken.None);

        source.CapturedTheme.Should().Be("Undead");
        source.CapturedCrGte.Should().Be(2.0);
        source.CapturedCrLte.Should().Be(6.0);
        source.CapturedEdition.Should().Be(DndVersion.Edition2014);
        source.CapturedSrdOnly.Should().BeFalse();
    }

    [Fact]
    public async Task BuildAsync_derives_a_sensible_default_cr_band_when_none_supplied()
    {
        var source = new FakeMonsterSource(FiveCr3Monsters());
        var generator = new EncounterGenerator(source, new EncounterAssessor());

        await generator.BuildAsync(
            Party4L5, Difficulty.Trivial, DndVersion.Edition2014, theme: null, crLte: null, crGte: null, CancellationToken.None);

        source.CapturedCrGte.Should().NotBeNull();
        source.CapturedCrLte.Should().NotBeNull();
        source.CapturedCrGte!.Value.Should().BeGreaterThanOrEqualTo(0);
        source.CapturedCrGte!.Value.Should().BeLessThan(source.CapturedCrLte!.Value);
    }

    [Fact]
    public async Task BuildAsync_falls_back_to_the_closest_set_when_candidates_are_sparse()
    {
        IReadOnlyList<MonsterRef> pool = [new MonsterRef("mm.monster.tiny", "Tiny Monster", 0.125, EncounterMath.CrToXp(0.125))];
        var source = new FakeMonsterSource(pool);
        var generator = new EncounterGenerator(source, new EncounterAssessor());

        var result = await generator.BuildAsync(
            Party4L5, Difficulty.Hard, DndVersion.Edition2014, theme: null, crLte: null, crGte: null, CancellationToken.None);

        result.FullyMatched.Should().BeFalse();
        result.Note.Should().NotBeNullOrWhiteSpace();
        result.Assessment.Monsters.Should().ContainSingle(m => m.Id == "mm.monster.tiny");
    }

    [Fact]
    public async Task BuildAsync_targets_the_assessed_band_not_raw_xp_across_the_2014_multiplier_step()
    {
        // Party 4x L5 budget (2014): Easy=1000, Medium=2000, Hard=3000, Deadly=4400.
        // 2 CR-3 (700xp) monsters: raw total 1400 (below the raw Medium threshold of 2000), but
        // the 2-monster x1.5 multiplier already adjusts it to 2100 -> assessed Medium.
        // A naive "keep adding until raw XP clears the threshold" approach would add a 3rd
        // monster (raw 2100 >= 2000) -- but the 3-monster x2.0 multiplier then adjusts that to
        // 4200, overshooting straight into Hard. Targeting the ASSESSED band must stop at 2.
        var source = new FakeMonsterSource(FiveCr3Monsters());
        var generator = new EncounterGenerator(source, new EncounterAssessor());

        var result = await generator.BuildAsync(
            Party4L5, Difficulty.Medium, DndVersion.Edition2014, theme: null, crLte: null, crGte: null, CancellationToken.None);

        result.Assessment.Difficulty.Should().Be(Difficulty.Medium);
        result.Assessment.Monsters.Should().HaveCount(2);
        result.FullyMatched.Should().BeTrue();
    }

    [Fact]
    public async Task BuildAsync_rejects_overshooting_candidates_and_reports_an_overshoot_worded_note()
    {
        // Party 4x L5 budget (2014): Easy=1000, Medium=2000, Hard=3000, Deadly=4400.
        // Only CR-3 (700xp) monsters are available. 1 monster -> raw 700, x1.0 multiplier
        // (1-monster step) -> adjusted 700, which is below Easy's 1000 threshold (Trivial).
        // Every remaining candidate is identical, so trying a 2nd -> raw 1400, x1.5 multiplier
        // (2-monster step) -> adjusted 2100, which is >= Medium's 2000 threshold: past the Easy
        // target. The overshoot guard must reject every such 2nd-monster trial and stop the
        // build at 1 monster (Trivial) rather than overshoot into Medium — or, if the guard kept
        // adding regardless, compound further into Hard/Deadly as later multiplier steps apply.
        var source = new FakeMonsterSource(FiveCr3Monsters());
        var generator = new EncounterGenerator(source, new EncounterAssessor());

        var result = await generator.BuildAsync(
            Party4L5, Difficulty.Easy, DndVersion.Edition2014, theme: null, crLte: null, crGte: null, CancellationToken.None);

        // Never overshoot: whatever the generator returns must never be assessed above the
        // requested target band, even when the target itself turns out to be unreachable.
        ((int)result.Assessment.Difficulty).Should().BeLessThanOrEqualTo((int)Difficulty.Easy);
        result.Assessment.Monsters.Should().HaveCount(1);
        result.FullyMatched.Should().BeFalse();

        // The fallback Note must name the real stopping reason (overshoot-blocked), not the
        // scarcity wording used when the loop simply runs out of candidates.
        result.Note.Should().NotBeNullOrWhiteSpace();
        result.Note.Should().Contain("overshoot");
        result.Note.Should().NotContain("candidate(s) in CR");
    }
}