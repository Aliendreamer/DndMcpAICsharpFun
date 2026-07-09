namespace DndMcpAICsharpFun.Domain;

public enum IngestionStatus
{
    Pending,
    Processing,
    Failed,
    Duplicate,
    JsonIngested,
    EntitiesExtracting,
    EntitiesExtracted,
    EntitiesIngesting,
    EntitiesIngested,
    EntitiesFailed,
}