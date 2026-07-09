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


    /// <summary>
    /// Gates a high-CR monster behind a minimum requested <c>crLte</c>: returns the gated
    /// monster (twice, so the greedy loop can stack two of it) only when the caller's crLte is
    /// at or above <paramref name="gateCrLte"/>; otherwise returns only weak CR-1 filler that can
    /// never climb to Deadly even by piling on MaxMonsters of it. This is how the FIX-1 test
    /// proves the generator actually widened its default CR ceiling — the old avg-level cap
    /// would never clear the gate, so the build would fall back to the weak filler and miss
    /// Deadly; the new band-derived ceiling clears it.
    /// </summary>
    private sealed class CrGatedMonsterSource(double gateCrLte, MonsterRef gatedMonster) : IEncounterMonsterSource
    {
        public double? CapturedCrLte { get; private set; }

        public Task<IReadOnlyList<MonsterRef>> FindAsync(
            DndVersion ed, double crGte, double crLte, string? theme, bool srdOnly, int limit, CancellationToken ct)
        {
            CapturedCrLte = crLte;
            IReadOnlyList<MonsterRef> pool = crLte >= gateCrLte
                ? [gatedMonster, gatedMonster with { Id = gatedMonster.Id + "-2" }]
                : [new MonsterRef("mm.monster.weak-filler", "Weak Filler", 1, EncounterMath.CrToXp(1))];
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


    [Fact]
    public async Task BuildAsync_widens_the_default_cr_ceiling_so_a_high_level_party_can_reach_deadly()
    {
        // Party 4x L15 (2014) Deadly budget = 4 x 6400 = 25600 total party XP. The OLD default
        // cap (Math.Max(1, avgLevel) = 15) would request crLte=15 from the source, never
        // clearing this gate — the build would only ever see weak CR-1 filler and could never
        // reach Deadly, however many it stacked (multiplier-adjusted total tops out at 200 * 15
        // monsters * 4.0 = 12000, still short of 25600). The NEW default derives crLte from the
        // Deadly band's own XP budget (highest CR whose EncounterMath.CrToXp <= 25600 is CR 20 @
        // 25000 XP), which clears the gate and lets two CR-20 monsters stack past the Deadly
        // threshold (2-monster x1.5 multiplier: 50000 -> adjusted 75000 >= 25600).
        IReadOnlyList<int> party4L15 = [15, 15, 15, 15];
        var gatedMonster = new MonsterRef("mm.monster.ancient-horror", "Ancient Horror", 20, EncounterMath.CrToXp(20));
        var source = new CrGatedMonsterSource(gateCrLte: 20.0, gatedMonster);
        var generator = new EncounterGenerator(source, new EncounterAssessor());

        var result = await generator.BuildAsync(
            party4L15, Difficulty.Deadly, DndVersion.Edition2014, theme: null, crLte: null, crGte: null, CancellationToken.None);

        // Proves the widened ceiling was actually requested from the source, not just that the
        // build happened to succeed for some unrelated reason.
        source.CapturedCrLte.Should().BeGreaterThanOrEqualTo(20.0);
        result.Assessment.Difficulty.Should().Be(Difficulty.Deadly);
        result.FullyMatched.Should().BeTrue();
    }

    [Fact]
    public async Task BuildAsync_widens_the_default_ceiling_to_honor_an_explicit_floor_the_default_would_undercut()
    {
        // 4xL1 party, Easy target (2014): total party budget = 4 * 25 = 100 XP. The DERIVED
        // default ceiling from that budget (EncounterMath.HighestCrAtOrBelowXp(100) = CR 0.5)
        // sits far below an explicit CR-5 floor supplied via crGte (the MCP tool's minCr).
        // Each bound falling back to its own independent default -- without a cross-clamp --
        // would pass the source an INVERTED range (crGte=5 > crLte=0.5): against the real
        // source that returns zero candidates and produces a confusing "0 candidate(s) in CR
        // [5, 0.5]" note instead of honoring the caller's explicit floor. The fix must widen
        // the defaulted ceiling to match the explicit floor so the range handed to the source
        // is never inverted.
        IReadOnlyList<int> party4L1 = [1, 1, 1, 1];
        var source = new FakeMonsterSource(FiveCr3Monsters());
        var generator = new EncounterGenerator(source, new EncounterAssessor());

        await generator.BuildAsync(
            party4L1, Difficulty.Easy, DndVersion.Edition2014, theme: null, crLte: null, crGte: 5.0, CancellationToken.None);

        source.CapturedCrGte.Should().Be(5.0);
        source.CapturedCrLte.Should().BeGreaterThanOrEqualTo(source.CapturedCrGte!.Value);
    }

    [Fact]
    public async Task BuildAsync_throws_when_both_bounds_are_explicit_and_inverted()
    {
        var source = new FakeMonsterSource(FiveCr3Monsters());
        var generator = new EncounterGenerator(source, new EncounterAssessor());

        var act = async () => await generator.BuildAsync(
            Party4L5, Difficulty.Medium, DndVersion.Edition2014, theme: null, crLte: 2.0, crGte: 10.0, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }
}