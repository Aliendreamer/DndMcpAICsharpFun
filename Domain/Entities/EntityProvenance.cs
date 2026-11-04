namespace DndMcpAICsharpFun.Domain.Entities;

public sealed record PageBox(int X0, int Y0, int X1, int Y1);

public sealed record ProvenancePage(int Page, IReadOnlyList<PageBox> Boxes);

/// <summary>Where on the source PDF an entity was read from (pages and bounding boxes).</summary>
public sealed record EntityProvenance(IReadOnlyList<ProvenancePage> Pages);
