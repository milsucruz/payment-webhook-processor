using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Payments.Api.Tests.Webhooks;

public sealed class CollectingLoggerProvider : ILoggerProvider
{
    private readonly string category;
    private readonly TaskCompletionSource<LogLevel> firstEntry = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public CollectingLoggerProvider(string category)
    {
        this.category = category;
    }

    public Task<LogLevel> FirstEntry => this.firstEntry.Task;

    public ILogger CreateLogger(string categoryName) =>
        categoryName == this.category ? new CollectingLogger(this.firstEntry) : NullLogger.Instance;

    public void Dispose()
    {
    }

    private sealed class CollectingLogger : ILogger
    {
        private readonly TaskCompletionSource<LogLevel> firstEntry;

        public CollectingLogger(TaskCompletionSource<LogLevel> firstEntry)
        {
            this.firstEntry = firstEntry;
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            this.firstEntry.TrySetResult(logLevel);
    }
}
