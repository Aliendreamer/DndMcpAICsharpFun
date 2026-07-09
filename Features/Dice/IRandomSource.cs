namespace DndMcpAICsharpFun.Features.Dice;

public interface IRandomSource
{
    int Next(int minInclusive, int maxExclusive);
}

public sealed class SystemRandomSource : IRandomSource
{
    public int Next(int minInclusive, int maxExclusive) => Random.Shared.Next(minInclusive, maxExclusive);
}
