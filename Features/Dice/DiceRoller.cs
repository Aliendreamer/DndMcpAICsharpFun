namespace DndMcpAICsharpFun.Features.Dice;

public sealed class DiceRoller(IRandomSource rng)
{
    public RollResult Roll(DiceExpression e)
    {
        if (e.Mode == RollMode.Normal)
        {
            var dice = new int[e.Count];
            for (var i = 0; i < e.Count; i++)
                dice[i] = rng.Next(1, e.Die + 1);

            var total = dice.Sum() + e.Modifier;
            var modStr = FormatModifier(e.Modifier);
            var prefix = e.Count == 1 ? $"d{e.Die}" : $"{e.Count}d{e.Die}";
            var breakdown = $"{prefix}{modStr} → [{string.Join(",", dice)}]{modStr} = {total}";

            return new RollResult(e, dice, dice, e.Modifier, total, breakdown);
        }

        var a = rng.Next(1, e.Die + 1);
        var b = rng.Next(1, e.Die + 1);
        var kept = e.Mode == RollMode.Advantage ? Math.Max(a, b) : Math.Min(a, b);
        var advOrDis = e.Mode == RollMode.Advantage ? "adv" : "dis";
        var total2 = kept + e.Modifier;
        var tail = e.Modifier == 0 ? $"{kept}" : $"{kept}{FormatModifier(e.Modifier)} = {total2}";
        var breakdown2 = $"d{e.Die} ({advOrDis}) → [{a},{b}] → {tail}";

        return new RollResult(e, new[] { a, b }, new[] { kept }, e.Modifier, total2, breakdown2);
    }

    private static string FormatModifier(int modifier) =>
        modifier switch
        {
            0 => string.Empty,
            > 0 => $"+{modifier}",
            _ => modifier.ToString(),
        };
}
