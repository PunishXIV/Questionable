namespace Questionable.Utils;

internal sealed class RecentLog(Func<string, string>? category = null) : ILoggerProvider
{
    private const int Capacity = 40;
    private const int LineCapacity = 5000;

    private readonly List<Entry> _entries = [];
    private readonly Queue<string> _lines = new();

    public ILogger CreateLogger(string categoryName) => new Logger(this, category?.Invoke(categoryName) ?? categoryName);

    public void Dispose()
    {
    }

    internal IReadOnlyList<Entry> GetEntries()
    {
        lock (_entries)
            return [.. _entries];
    }

    // dalamud.log stops growing at Dalamud's 100 MB limit, so long sessions need Questionable's own copy.
    internal IReadOnlyList<string> GetLines()
    {
        lock (_lines)
            return [.. _lines];
    }

    internal void Add(LogLevel level, string message, DateTime time)
    {
        lock (_entries)
        {
            // Errors are what reports need most, so routine lines go first when the log is full.
            if (_entries.Count == Capacity)
            {
                LogLevel lowest = _entries.Min(x => x.Level);
                _entries.RemoveAt(_entries.FindIndex(x => x.Level == lowest));
            }

            _entries.Add(new(time, level, message));
        }
    }

    private void AddLine(string line)
    {
        lock (_lines)
        {
            if (_lines.Count == LineCapacity)
                _lines.Dequeue();
            _lines.Enqueue(line);
        }
    }

    internal readonly record struct Entry(DateTime Time, LogLevel Level, string Message);

    private sealed class Logger(RecentLog log, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel is >= LogLevel.Debug and < LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            string message = formatter(state, exception);
            log.AddLine(string.Create(CultureInfo.InvariantCulture,
                $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{Short(logLevel)}] [Questionable] {category}: {message}{(exception != null ? "\n" + exception : "")}"));

            if (logLevel < LogLevel.Information)
                return;

            if (exception != null)
                message += $" | {exception.GetType().Name}: {exception.Message}";
            log.Add(logLevel, message, DateTime.UtcNow);
        }

        private static string Short(LogLevel level) => level switch
        {
            LogLevel.Debug => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            _ => "FTL",
        };
    }
}
