namespace DndMcpAICsharpFun.Domain.Entities;

public sealed record FirstAppearance(string Book, string Edition, int? Page = null);

public sealed record Revision(string Book, string Edition, string Summary);