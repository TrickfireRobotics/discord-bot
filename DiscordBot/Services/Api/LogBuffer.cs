using Microsoft.Extensions.Logging;

namespace DiscordBot.Services.Api;

public record LogLine(DateTimeOffset Time, string Level, string Category, string Message);

/// <summary>
/// Holds the most recent log lines in memory so the dashboard can tail them.
/// Deliberately not persisted - logs are for debugging the running process,
/// and Docker already keeps its own copy.
/// </summary>
public class LogBuffer
{
    private readonly Queue<LogLine> _lines = new();

    public int Capacity { get; init; } = 500;

    public void Add(LogLine line)
    {
        lock (_lines)
        {
            _lines.Enqueue(line);
            while (_lines.Count > Capacity)
            {
                _lines.Dequeue();
            }
        }
    }

    /// <summary>
    /// The most recent lines, newest first.
    /// </summary>
    public IReadOnlyList<LogLine> Recent(int limit)
    {
        lock (_lines)
        {
            return _lines.Reverse().Take(limit).ToList();
        }
    }
}

/// <summary>
/// Feeds every log message the host emits into a <see cref="LogBuffer"/>.
/// </summary>
public sealed class LogBufferProvider(LogBuffer buffer) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new BufferLogger(buffer, categoryName);

    public void Dispose() { }

    private sealed class BufferLogger(LogBuffer buffer, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Debug;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            string message = formatter(state, exception);
            if (exception is not null)
            {
                message += $"\n{exception}";
            }

            buffer.Add(new LogLine(DateTimeOffset.UtcNow, logLevel.ToString(), category, message));
        }
    }
}
