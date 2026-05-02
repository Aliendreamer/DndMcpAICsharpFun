using System.ComponentModel.DataAnnotations;

namespace DndMcpAICsharpFun.Infrastructure.Sqlite;

public sealed class IngestionRecord
{
    public int Id { get; set; }

    [Required, MaxLength(1024)]
    public string FilePath { get; set; } = string.Empty;

    [Required, MaxLength(512)]
    public string FileName { get; set; } = string.Empty;

    [MaxLength(64)]
    public string FileHash { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string SourceName { get; set; } = string.Empty;

    [Required, MaxLength(20)]
    public string Version { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string DisplayName { get; set; } = string.Empty;

    public IngestionStatus Status { get; set; } = IngestionStatus.Pending;

    public string? Error { get; set; }

    public int? ChunkCount { get; set; }

    public DateTime? IngestedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
