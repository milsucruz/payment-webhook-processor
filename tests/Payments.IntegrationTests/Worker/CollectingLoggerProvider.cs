using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Payments.IntegrationTests.Worker;

public sealed class CollectingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<string> messages = new();

    public IReadOnlyCollection<string> Messages => this.messages;

    public ILogger CreateLogger(string categoryName) => new CollectingLogger(this.messages);

    public void Dispose()
    {
    }

    private sealed class CollectingLogger : ILogger
    {
        private readonly ConcurrentQueue<string> messages;

        public CollectingLogger(ConcurrentQueue<string> messages)
        {
            this.messages = messages;
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            this.messages.Enqueue(formatter(state, exception));
    }
}
