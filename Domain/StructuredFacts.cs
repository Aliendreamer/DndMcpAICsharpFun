namespace DndMcpAICsharpFun.Domain;

/// <summary>
/// A named table extracted from a D&amp;D source book (e.g. "Dragonborn Draconic Ancestry").
/// ColumnsJson holds the column header array as a JSON string.
/// </summary>
public sealed class StructuredTable
{
    public long Id { get; set; }

    public string CanonicalId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>JSON array of column header strings.</summary>
    public string ColumnsJson { get; set; } = string.Empty;

    public string SourceBook { get; set; } = string.Empty;
}

/// <summary>
/// One row in a <see cref="StructuredTable"/>. RowIndex is 0-based.
/// CellsJson holds the cell values as a JSON array of strings.
/// </summary>
public sealed class StructuredTableRow
{
    public long Id { get; set; }

    public long TableId { get; set; }

    public int RowIndex { get; set; }

    /// <summary>JSON array of cell values (parallel to the parent table's columns).</summary>
    public string CellsJson { get; set; } = string.Empty;
}

/// <summary>
/// A "choose one from a list" set (e.g. tool proficiency options, background feature choices).
/// OptionsJson holds the option strings as a JSON array.
/// </summary>
public sealed class ChoiceSetRow
{
    public long Id { get; set; }

    public string CanonicalId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>JSON array of option strings.</summary>
    public string OptionsJson { get; set; } = string.Empty;
}