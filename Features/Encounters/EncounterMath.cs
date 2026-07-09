using DndMcpAICsharpFun.Domain;

namespace DndMcpAICsharpFun.Features.Encounters;

/// <summary>
/// Pure, table-driven encounter-building math shared by both D&amp;D editions
/// (<see cref="DndVersion.Edition2014"/> and <see cref="DndVersion.Edition2024"/>).
/// No I/O, no DI — everything here is a deterministic function of its inputs so it can be
/// unit-tested against the published DMG tables without any infrastructure.
/// </summary>
public static class EncounterMath
{
    /// <summary>
    /// Standard Challenge Rating → XP table (DMG), CR 0 through 30 inclusive.
    /// </summary>
    private static readonly IReadOnlyDictionary<double, int> CrToXpTable = new Dictionary<double, int>
    {
        [0] = 10,
        [0.125] = 25,
        [0.25] = 50,
        [0.5] = 100,
        [1] = 200,
        [2] = 450,
        [3] = 700,
        [4] = 1100,
        [5] = 1800,
        [6] = 2300,
        [7] = 2900,
        [8] = 3900,
        [9] = 5000,
        [10] = 5900,
        [11] = 7200,
        [12] = 8400,
        [13] = 10000,
        [14] = 11500,
        [15] = 13000,
        [16] = 15000,
        [17] = 18000,
        [18] = 20000,
        [19] = 22000,
        [20] = 25000,
        [21] = 33000,
        [22] = 41000,
        [23] = 50000,
        [24] = 62000,
        [25] = 75000,
        [26] = 90000,
        [27] = 105000,
        [28] = 120000,
        [29] = 135000,
        [30] = 155000
    };

    /// <summary>
    /// 2014 DMG p.82 "Character Advancement" XP thresholds, per character, by level (1..20).
    /// Columns: [Easy, Medium, Hard, Deadly].
    /// </summary>
    private static readonly int[][] Thresholds2014PerLevel =
    [
        [25, 50, 75, 100],          // L1
        [50, 100, 150, 200],        // L2
        [75, 150, 225, 400],        // L3
        [125, 250, 375, 500],       // L4
        [250, 500, 750, 1100],      // L5
        [300, 600, 900, 1400],      // L6
        [350, 750, 1100, 1700],     // L7
        [450, 900, 1400, 2100],     // L8
        [550, 1100, 1600, 2400],    // L9
        [600, 1200, 1900, 2800],    // L10
        [800, 1600, 2400, 3600],    // L11
        [1000, 2000, 3000, 4500],   // L12
        [1100, 2200, 3400, 5100],   // L13
        [1250, 2500, 3800, 5700],   // L14
        [1400, 2800, 4300, 6400],   // L15
        [1600, 3200, 4800, 7200],   // L16
        [2000, 3900, 5900, 8800],   // L17
        [2100, 4200, 6300, 9500],   // L18
        [2400, 4900, 7300, 10900],  // L19
        [2800, 5700, 8500, 12700]   // L20
    ];

    /// <summary>
    /// 2024 DMG "Encounter Building" XP budget, per character, by level (1..20).
    /// Columns: [Low, Moderate, High].
    /// </summary>
    private static readonly int[][] Budgets2024PerLevel =
    [
        [50, 75, 100],        // L1
        [100, 150, 200],      // L2
        [150, 225, 400],      // L3
        [250, 375, 500],      // L4
        [500, 750, 1100],     // L5
        [600, 1000, 1400],    // L6
        [750, 1300, 1700],    // L7
        [1000, 1700, 2100],   // L8
        [1300, 2000, 2600],   // L9
        [1600, 2300, 3100],   // L10
        [1900, 2900, 4100],   // L11
        [2200, 3700, 4700],   // L12
        [2600, 4200, 5400],   // L13
        [2900, 4900, 6200],   // L14
        [3300, 5400, 7800],   // L15
        [3800, 6100, 9800],   // L16
        [4500, 7200, 11700],  // L17
        [5000, 8700, 14200],  // L18
        [5500, 10700, 17200], // L19
        [6400, 13200, 22000]  // L20
    ];

    /// <summary>
    /// 2014 monster-count multiplier steps (DMG p.82), in ascending order.
    /// </summary>
    private static readonly double[] Multiplier2014Steps = [1.0, 1.5, 2.0, 2.5, 3.0, 4.0];

    /// <summary>
    /// Looks up the standard XP award for a given Challenge Rating (0, 1/8, 1/4, 1/2, 1..30).
    /// </summary>
    public static int CrToXp(double cr) =>
        CrToXpTable.TryGetValue(cr, out var xp)
            ? xp
            : throw new ArgumentOutOfRangeException(nameof(cr), cr, "CR must be 0, 1/8, 1/4, 1/2, or an integer 1..30.");

    /// <summary>
    /// Sums the per-character XP thresholds/budgets for the whole party.
    /// 2014 returns 4 columns [Easy, Medium, Hard, Deadly]; 2024 returns 3 columns [Low, Moderate, High].
    /// </summary>
    public static int[] PartyBudget(IReadOnlyList<int> levels, DndVersion ed)
    {
        ArgumentNullException.ThrowIfNull(levels);

        var table = ed == DndVersion.Edition2014 ? Thresholds2014PerLevel : Budgets2024PerLevel;
        var totals = new int[table[0].Length];

        foreach (var level in levels)
        {
            if (level < 1 || level > 20)
            {
                throw new ArgumentOutOfRangeException(nameof(levels), level, "Character level must be between 1 and 20.");
            }

            var row = table[level - 1];
            for (var i = 0; i < row.Length; i++)
            {
                totals[i] += row[i];
            }
        }

        return totals;
    }

    /// <summary>
    /// 2014 DMG monster-count XP multiplier, shifted by party size (2024 is always 1.0 —
    /// its budget already accounts for multiple monsters).
    /// </summary>
    public static double Multiplier(int monsterCount, int partySize, DndVersion ed)
    {
        if (ed == DndVersion.Edition2024)
        {
            return 1.0;
        }

        var baseIndex = monsterCount switch
        {
            <= 1 => 0,
            2 => 1,
            >= 3 and <= 6 => 2,
            >= 7 and <= 10 => 3,
            >= 11 and <= 14 => 4,
            _ => 5
        };

        var shift = partySize < 3 ? 1 : partySize >= 6 ? -1 : 0;
        var index = Math.Clamp(baseIndex + shift, 0, Multiplier2014Steps.Length - 1);
        return Multiplier2014Steps[index];
    }

    /// <summary>
    /// Classifies an encounter's total monster XP against the party's budget.
    /// 2014: classifies <c>round(totalMonsterXp × Multiplier(...))</c> against [Easy, Medium, Hard, Deadly].
    /// 2024: classifies raw <paramref name="totalMonsterXp"/> against [Low, Moderate, High]
    /// (mapped to Easy/Medium/Hard bands — 2024 has no separate Deadly band).
    /// </summary>
    public static Difficulty Classify(int totalMonsterXp, IReadOnlyList<int> levels, int monsterCount, DndVersion ed)
    {
        var budget = PartyBudget(levels, ed);

        if (ed == DndVersion.Edition2014)
        {
            var multiplier = Multiplier(monsterCount, levels.Count, ed);
            var adjusted = (int)Math.Round(totalMonsterXp * multiplier, MidpointRounding.AwayFromZero);

            if (adjusted >= budget[3]) return Difficulty.Deadly;
            if (adjusted >= budget[2]) return Difficulty.Hard;
            if (adjusted >= budget[1]) return Difficulty.Medium;
            if (adjusted >= budget[0]) return Difficulty.Easy;
            return Difficulty.Trivial;
        }

        if (totalMonsterXp >= budget[2]) return Difficulty.Hard;
        if (totalMonsterXp >= budget[1]) return Difficulty.Medium;
        if (totalMonsterXp >= budget[0]) return Difficulty.Easy;
        return Difficulty.Trivial;
    }
}