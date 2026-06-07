using System.ComponentModel.DataAnnotations;

namespace DndMcpAICsharpFun.Infrastructure.Marker;

public sealed class MarkerOptions
{
    [Required]
    public string Url { get; set; } = "http://marker:5002";

    public int PollIntervalSeconds { get; set; } = 15;

    public int ConversionTimeoutMinutes { get; set; } = 240;

    /// <summary>
    /// The path inside the marker container where books are mounted.
    /// Used to map the host-side <c>filePath</c> → container path.
    /// </summary>
    public string BooksMountPath { get; set; } = "/books";
}
