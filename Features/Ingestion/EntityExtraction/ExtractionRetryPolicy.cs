namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

public sealed class ExtractionRetryPolicy
{
    public int MaxAttempts { get; init; } = 3;
    public TimeSpan BaseDelay { get; init; } = TimeSpan.FromSeconds(2);

    public async Task<T> ExecuteAsync<T>(
        Func<int, CancellationToken, Task<T>> operation,
        Func<T, bool> isSuccess,
        CancellationToken ct)
    {
        var lastResult = default(T)!;
        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            lastResult = await operation(attempt, ct);
            if (isSuccess(lastResult)) return lastResult;
            if (attempt < MaxAttempts)
                await Task.Delay(BaseDelay * Math.Pow(2, attempt - 1), ct);
        }
        return lastResult;
    }
}