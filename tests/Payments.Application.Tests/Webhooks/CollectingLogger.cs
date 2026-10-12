using Microsoft.Extensions.Logging;

namespace Payments.Application.Tests.Webhooks;

public sealed class CollectingLogger<T> : ILogger<T>
{
    public List<LogEntry> Entries { get; } = [];

    public IEnumerable<LogLevel> Levels => this.Entries.Select(entry => entry.Level);

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        this.Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
}

public sealed record LogEntry(LogLevel Level, string Message);
