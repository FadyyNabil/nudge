using System.Collections.Concurrent;
using System.Text;

namespace Marketplace.Services;

/// <summary>
/// Tiny rolling file logger. Writes one file per day (app-YYYYMMDD.log) into the log folder,
/// keeps the last 14 days. Needed because the free host gives no log viewer. Never throws.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _dir;
    private readonly LogLevel _min;
    private static readonly object Gate = new();
    private DateTime _lastCleanup = DateTime.MinValue;

    public FileLoggerProvider(string dir, LogLevel min)
    {
        _dir = dir; _min = min;
        try { Directory.CreateDirectory(dir); } catch { }
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);
    public void Dispose() { }

    internal bool Enabled(LogLevel l) => l >= _min && l != LogLevel.None;

    internal void Write(string category, LogLevel level, string message, Exception? ex)
    {
        try
        {
            var sb = new StringBuilder();
            sb.Append(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")).Append("Z [").Append(level.ToString().ToUpperInvariant()).Append("] ")
              .Append(category).Append(": ").Append(message.Replace("\r", " ").Replace("\n", " "));
            if (ex is not null) sb.AppendLine().Append(ex.ToString());
            sb.AppendLine();
            lock (Gate)
            {
                File.AppendAllText(Path.Combine(_dir, $"app-{DateTime.UtcNow:yyyyMMdd}.log"), sb.ToString());
                if (DateTime.UtcNow - _lastCleanup > TimeSpan.FromHours(6))
                {
                    _lastCleanup = DateTime.UtcNow;
                    foreach (var f in Directory.GetFiles(_dir, "app-*.log"))
                        if (File.GetLastWriteTimeUtc(f) < DateTime.UtcNow.AddDays(-14)) File.Delete(f);
                }
            }
        }
        catch { }
    }

    private sealed class FileLogger : ILogger
    {
        private readonly FileLoggerProvider _p; private readonly string _c;
        public FileLogger(FileLoggerProvider p, string c) { _p = p; _c = c; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => _p.Enabled(logLevel);
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            _p.Write(_c, logLevel, formatter(state, exception), exception);
        }
    }
}
