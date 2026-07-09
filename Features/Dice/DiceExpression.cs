using System.Text.RegularExpressions;

namespace DndMcpAICsharpFun.Features.Dice;

public enum RollMode
{
    Normal,
    Advantage,
    Disadvantage,
}

public readonly partial record struct DiceExpression(int Count, int Die, int Modifier, RollMode Mode)
{
    public const int MaxCount = 100;

    public static readonly int[] Dice = { 4, 6, 8, 10, 12, 20, 100 };

    [GeneratedRegex(
        @"^\s*(\d+)?d(4|6|8|10|12|20|100)\s*([+-]\s*\d+)?\s*(adv|dis|advantage|disadvantage)?\s*$",
        RegexOptions.IgnoreCase)]
    private static partial Regex ExpressionPattern();

    public static bool TryParse(string input, out DiceExpression expr, out string? error)
    {
        expr = default;
        error = null;

        if (string.IsNullOrWhiteSpace(input))
        {
            error = "Dice expression cannot be empty.";
            return false;
        }

        var match = ExpressionPattern().Match(input);
        if (!match.Success)
        {
            error = $"Could not parse dice expression '{input}'. Expected format like '2d6+3'.";
            return false;
        }

        var count = 1;
        if (match.Groups[1].Success && !int.TryParse(match.Groups[1].Value, out count))
        {
            error = $"Dice count out of range: '{match.Groups[1].Value}'.";
            return false;
        }

        var die = int.Parse(match.Groups[2].Value);

        var modifier = 0;
        if (match.Groups[3].Success)
        {
            var modText = match.Groups[3].Value.Replace(" ", string.Empty);
            if (!int.TryParse(modText, out modifier))
            {
                error = $"Dice modifier out of range: '{modText}'.";
                return false;
            }
        }

        var mode = RollMode.Normal;
        if (match.Groups[4].Success)
        {
            var modeText = match.Groups[4].Value.ToLowerInvariant();
            mode = modeText is "adv" or "advantage" ? RollMode.Advantage : RollMode.Disadvantage;
        }

        if (count < 1 || count > MaxCount)
        {
            error = $"Dice count must be between 1 and {MaxCount} (got {count}).";
            return false;
        }

        if (mode != RollMode.Normal && (die != 20 || count != 1))
        {
            error = "Advantage/disadvantage is only valid for a single d20 roll.";
            return false;
        }

        expr = new DiceExpression(count, die, modifier, mode);
        return true;
    }

    public static DiceExpression Parse(string input)
    {
        if (!TryParse(input, out var expr, out var error))
        {
            throw new FormatException(error);
        }

        return expr;
    }
}