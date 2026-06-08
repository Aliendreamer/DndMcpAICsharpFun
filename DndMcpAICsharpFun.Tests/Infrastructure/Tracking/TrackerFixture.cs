using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using DndMcpAICsharpFun.Tests.Persistence;

namespace DndMcpAICsharpFun.Tests.Infrastructure.Tracking;

/// <summary>Builds ingestion trackers over the shared Postgres test container.</summary>
public sealed class TrackerFixture(PostgresFixture pg)
{
    public SqliteIngestionTracker CreateTracker() => new(pg.NewContext());

    public static IngestionRecord SampleRecord() => new()
    {
        FilePath = "/tmp/test.pdf",
        FileName = "test.pdf",
        FileHash = string.Empty,
        Version = "5e",
        DisplayName = "Player's Handbook",
        Status = IngestionStatus.Pending,
    };
}
