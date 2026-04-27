namespace DndMcpAICsharpFun.Infrastructure.Sqlite;

public enum IngestionStatus
{
    Pending,
    Processing,
    Completed,
    Failed,
    Duplicate
}
