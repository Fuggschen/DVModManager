using Microsoft.Extensions.Logging;

namespace DVModManager.Services;

/// <summary>
/// A simple rolling file logger that writes to %LocalAppData%\DVModManager\logs\dvmm-YYYY-MM-DD.log.
/// One file per calendar day; old entries are appended so a single crash doesn't lose context.
/// Thread-safe via a dedicated background writer queue.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _logDirectory;
    private readonly LogLevel _minLevel;
    private readonly System.Collections.Concurrent.BlockingCollection<string> _queue = new(4096);
    private readonly Thread _writerThread;
    private bool _disposed;

    public FileLoggerProvider(string logDirectory, LogLevel minLevel = LogLevel.Debug)
    {
        _logDirectory = logDirectory;
        _minLevel = minLevel;
        try { Directory.CreateDirectory(logDirectory); }
        catch { /* log directory unavailable – file logging silently disabled */ }

        _writerThread = new Thread(WriterLoop) { IsBackground = true, Name = "FileLogger" };
        _writerThread.Start();
    }

    public ILogger CreateLogger(string categoryName) =>
        new FileLogger(categoryName, _minLevel, Enqueue);

    public void Enqueue(string line) => _queue.TryAdd(line);

    private void WriterLoop()
    {
        foreach (var line in _queue.GetConsumingEnumerable())
        {
            try
            {
                var path = Path.Combine(_logDirectory,
                    $"dvmm-{DateTime.Now:yyyy-MM-dd}.log");
                File.AppendAllText(path, line + Environment.NewLine);
            }
            catch { /* never crash the logger */ }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _queue.CompleteAdding();
        _writerThread.Join(TimeSpan.FromSeconds(2));
        _queue.Dispose();
    }

    private sealed class FileLogger(
        string category,
        LogLevel minLevel,
        Action<string> enqueue) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => level >= minLevel;

        public void Log<TState>(
            LogLevel level, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(level)) return;

            var prefix = level switch
            {
                LogLevel.Trace       => "TRC",
                LogLevel.Debug       => "DBG",
                LogLevel.Information => "INF",
                LogLevel.Warning     => "WRN",
                LogLevel.Error       => "ERR",
                LogLevel.Critical    => "CRT",
                _                    => "???"
            };

            var shortCat = category.Contains('.')
                ? category[(category.LastIndexOf('.') + 1)..]
                : category;

            var message = formatter(state, exception);
            var line = $"[{DateTime.Now:HH:mm:ss.fff}] [{prefix}] [{shortCat}] {message}";
            if (exception != null)
                line += Environment.NewLine + exception.ToString();

            enqueue(line);
        }
    }
}
