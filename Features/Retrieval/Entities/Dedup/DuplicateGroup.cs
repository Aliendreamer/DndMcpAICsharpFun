namespace DndMcpAICsharpFun.Features.Retrieval.Entities.Dedup;

/// <summary>One dedup group: the surviving winner and the loser ids to be removed.</summary>
public sealed record DuplicateGroup(string Key, string WinnerId, IReadOnlyList<string> LoserIds);

/// <summary>Corpus-wide duplicate scan result.</summary>
public sealed record DuplicateReport(int GroupCount, int LoserCount, IReadOnlyList<DuplicateGroup> Groups);