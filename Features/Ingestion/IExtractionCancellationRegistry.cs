namespace DndMcpAICsharpFun.Features.Ingestion;

public interface IExtractionCancellationRegistry
{
    void Register(int bookId, CancellationTokenSource cts);
    bool Cancel(int bookId);
    void Unregister(int bookId);
}
