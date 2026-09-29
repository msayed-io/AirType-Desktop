namespace LiveTypeBridge.Core.Logging;

/// <summary>
/// Tiny local file logger. Text content streamed from the phone is hidden by default
/// (only its length is recorded) unless the user explicitly enables content logging.
/// </summary>
public sealed class Log : IDisposable
{
    private readonly object _gate = new();
    private readonly string _folder;
    private volatile bool _enabled;
    private DateTime _currentDay;
    private StreamWriter? _writer;

    public Log(bool enabled)
    {
        _folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LiveTypeBridge", "logs");
        _enabled = enabled;
        _currentDay = DateTime.UtcNow.Date;
        Directory.CreateDirectory(_folder);
        PurgeOldLogs();
    }

    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    public string Folder => _folder;

    public void Info(string message) => Write("INFO", message);
    public void Warn(string message) => Write("WARN", message);
    public void Error(string message) => Write("ERROR", message);
    public void Debug(string message) => Write("DEBUG", message);

    /// <summary>Logs streamed text: content only when content logging is on, else just its length.</summary>
    public void StreamedText(string context, string text, bool contentLoggingEnabled)
    {
        if (!_enabled) return;
        var body = contentLoggingEnabled ? text.Replace("\n", "\\n") : $"<{text.Length} chars hidden>";
        Write("INFO", $"{context}: {body}");
    }

    private void Write(string level, string message)
    {
        if (!_enabled) return;
        try
        {
            lock (_gate)
            {
                EnsureWriter();
                _writer?.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}");
                _writer?.Flush();
            }
        }
        catch { /* logging must never break the app */ }
    }

    private void EnsureWriter()
    {
        var today = DateTime.UtcNow.Date;
        if (_currentDay != today)
        {
            _currentDay = today;
            _writer?.Dispose();
            _writer = null;
        }
        _writer ??= new StreamWriter(Path.Combine(_folder, $"LiveTypeBridge-{today:yyyyMMdd}.log"), append: true);
    }

    private void PurgeOldLogs()
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-7);
            foreach (var f in Directory.EnumerateFiles(_folder, "LiveTypeBridge-*.log"))
            {
                if (File.GetLastWriteTimeUtc(f) < cutoff)
                {
                    try { File.Delete(f); } catch { }
                }
            }
        }
        catch { }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }
}
