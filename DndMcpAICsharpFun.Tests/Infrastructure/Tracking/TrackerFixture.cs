using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using DndMcpAICsharpFun.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DndMcpAICsharpFun.Tests.Infrastructure.Tracking;

public sealed class TrackerFixture : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public TrackerFixture()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var db = new AppDbContext(_options);
        db.Database.Migrate();
    }

    public SqliteIngestionTracker CreateTracker()
    {
        var db = new AppDbContext(_options);
        return new SqliteIngestionTracker(db);
    }

    public static IngestionRecord SampleRecord() => new()
    {
        FilePath = "/tmp/test.pdf",
        FileName = "test.pdf",
        FileHash = string.Empty,
        Version = "5e",
        DisplayName = "Player's Handbook",
        Status = IngestionStatus.Pending,
    };

    public void Dispose() => _connection.Dispose();
}
