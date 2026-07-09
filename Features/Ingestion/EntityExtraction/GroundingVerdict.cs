namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

public enum GroundingStatus { Grounded, Ungrounded, Uncertain }

public readonly record struct GroundingVerdict(GroundingStatus Status, int DecidedByTier, double Score);

public readonly record struct Tier1Result(bool BelowFloor, double Score);
