namespace DndMcpAICsharpFun.Domain.Entities;

public sealed record PageBox(double X0, double Y0, double X1, double Y1);

public sealed record ProvenancePage(int Page, IReadOnlyList<PageBox> Boxes);

/// <summary>Where on the source PDF an entity was read from (pages and bounding boxes).</summary>
public sealed record EntityProvenance(IReadOnlyList<ProvenancePage> Pages);
