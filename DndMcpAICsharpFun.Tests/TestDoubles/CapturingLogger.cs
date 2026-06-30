using Microsoft.Extensions.Logging;

namespace DndMcpAICsharpFun.Tests.TestDoubles;

/// <summary>
/// A minimal <see cref="ILogger{T}"/> implementation that records every log call.
/// Designed for asserting warning / error emission in unit tests without external dependencies.
/// </summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly List<(LogLevel Level, string Message)> _logs = [];

    public IReadOnlyList<(LogLevel Level, string Message)> Logs => _logs;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        _logs.Add((logLevel, formatter(state, exception)));
    }
}
