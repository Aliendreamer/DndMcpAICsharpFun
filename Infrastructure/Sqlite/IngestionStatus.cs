namespace DndMcpAICsharpFun.Infrastructure.Sqlite;

public enum IngestionStatus
{
    Pending,
    Processing,
    Failed,
    Duplicate,
    JsonIngested,
}
