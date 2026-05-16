using System.ComponentModel.DataAnnotations;

namespace DndMcpAICsharpFun.Features.Search;

public sealed class SearXNGOptions
{
    [Required]
    public string Url { get; init; } = "http://searxng:8080";
    public int MaxResults { get; init; } = 5;
    public string[] AllowedDomains { get; init; } = [];
}
