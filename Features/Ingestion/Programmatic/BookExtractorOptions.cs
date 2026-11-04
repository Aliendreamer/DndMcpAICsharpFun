namespace DndMcpAICsharpFun.Features.Ingestion.Programmatic;

/// <summary>Bound from configuration section <c>BookExtractor</c>.</summary>
public sealed class BookExtractorOptions
{
    public string ServiceUrl { get; set; } = "http://localhost:8000";
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan Timeout { get; set; } = TimeSpan.FromHours(4);
}
