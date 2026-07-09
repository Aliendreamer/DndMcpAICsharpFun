namespace DndMcpAICsharpFun.Features.Dice;

public sealed record RollResult(
    DiceExpression Expression,
    IReadOnlyList<int> Dice,
    IReadOnlyList<int> Kept,
    int Modifier,
    int Total,
    string Breakdown);