namespace DndMcpAICsharpFun.Features.Retrieval;

/// <summary>How much of a long entity's text a search result carries.</summary>
public enum EntityTextMode
{
    /// <summary>The app picks the pieces that match the question, within a fixed budget.</summary>
    Pieces,
    /// <summary>The whole text of the named (or first) result when it fits
    /// <see cref="RetrievalOptions.EntityWholeMaxChars"/>; else Pieces. Raise the cap with the model's context.</summary>
    Whole,
    /// <summary>Pieces, plus a numbered list of the text's parts; the model fetches one with get_entity(id, part).</summary>
    ModelPicks,
}

public sealed class RetrievalOptions
{
    public float ScoreThreshold { get; set; } = 0.5f;
    public int MaxTopK { get; set; } = 20;
    public EntityTextMode EntityText { get; set; } = EntityTextMode.Whole;
    public int EntityWholeMaxChars { get; set; } = 14000;
}