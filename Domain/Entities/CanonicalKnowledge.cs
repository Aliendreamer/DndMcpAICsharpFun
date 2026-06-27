namespace DndMcpAICsharpFun.Domain.Entities;

/// <summary>A reference to the prose chunk a fact derives from (prose stays in Qdrant dnd_blocks).</summary>
public sealed record ProvenanceRef(string BlockId, string SourceBook, int? Page);

/// <summary>One table cell: a value plus optional provenance.</summary>
public sealed record CanonicalCell(string Value, ProvenanceRef? Provenance);

public sealed record CanonicalTableRow(IReadOnlyList<CanonicalCell> Cells);

/// <summary>A first-class relational table (named columns + rows of typed cells).</summary>
public sealed record CanonicalTable(
    string Id, string Name, IReadOnlyList<string> Columns, IReadOnlyList<CanonicalTableRow> Rows);

/// <summary>One option of a choice-set, pointing at a row of a table.</summary>
public sealed record CanonicalChoiceOption(string Key, string TableId, int RowIndex, ProvenanceRef? Provenance);

/// <summary>A first-class set of mutually-exclusive options (e.g. draconic ancestry).</summary>
public sealed record CanonicalChoiceSet(string Id, string Name, IReadOnlyList<CanonicalChoiceOption> Options);
