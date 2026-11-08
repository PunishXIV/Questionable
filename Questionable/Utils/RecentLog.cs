namespace Questionable.Utils;

// Memory only: the Stuck? report reads this so people don't have to dig through /xllog.
internal sealed class RecentLog : ILoggerProvider
{
    private const int Capacity = 40;

    private readonly Queue<Entry> _entries = new();

    public ILogger CreateLogger(string categoryName) => new Logger(this);

    public void Dispose()
    {
    }

    internal IReadOnlyList<Entry> GetEntries()
    {
        lock (_entries)
            return [.. _entries];
    }

    internal void Add(LogLevel level, string message, DateTime time)
    {
        lock (_entries)
        {
            if (_entries.Count == Capacity)
                _entries.Dequeue();
            _entries.Enqueue(new(time, level, message));
        }
    }

    internal readonly record struct Entry(DateTime Time, LogLevel Level, string Message);

    private sealed class Logger(RecentLog log) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel is >= LogLevel.Information and < LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            string message = formatter(state, exception);
            if (exception != null)
                message += $" | {exception.GetType().Name}: {exception.Message}";
            log.Add(logLevel, message, DateTime.UtcNow);
        }
    }
}
