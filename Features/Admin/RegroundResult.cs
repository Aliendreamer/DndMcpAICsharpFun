namespace DndMcpAICsharpFun.Features.Admin;

/// <summary>
/// Outcome of one <c>RegroundService.RegroundAsync</c> backlog pass over a single book's
/// NeedsReview entities (Task 7 of the entity-grounding-cascade effort).
/// </summary>
/// <param name="Scanned">Total entities flagged (Disposition==NeedsReview or legacy NeedsReview==true) at the start of the run.</param>
/// <param name="Promoted">Entities whose final disposition is Accepted (grounded, promoted out of the backlog).</param>
/// <param name="MarkedUngrounded">Entities whose final disposition is Ungrounded (Tier-2-judge-confirmed fabrication).</param>
/// <param name="StillFlagged">Entities left unchanged (still NeedsReview/Uncertain).</param>
/// <param name="Tier2Invoked">Number of entities for which the LLM judge (Tier 2) actually decided the verdict.</param>
public sealed record RegroundResult(int Scanned, int Promoted, int MarkedUngrounded, int StillFlagged, int Tier2Invoked);