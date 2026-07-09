using DndMcpAICsharpFun.Domain;

namespace DndMcpAICsharpFun.Domain;

public sealed class IngestionRecord
{
    public int Id { get; set; }

    public string FilePath { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public string FileHash { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public IngestionStatus Status { get; set; } = IngestionStatus.Pending;

    public string? Error { get; set; }

    public int? ChunkCount { get; set; }

    public int? EntityCount { get; set; }

    public DateTime? IngestedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public BookType BookType { get; set; } = BookType.Unknown;

    public string? FivetoolsSourceKey { get; set; }
}