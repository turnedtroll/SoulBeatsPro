namespace SoulBeatsPro;

/// <summary>
/// Lightweight structured logger.
///
/// - Appends every line to %LOCALAPPDATA%\bigbart\telemetry\macro.log
/// - Keeps the last 200 lines in a ring buffer for crash breadcrumbs
/// - Broadcasts every line to any subscribed listeners (used by the
///   remote log-tail command so the admin panel can watch live output)
///
/// Never throws — logging must never break the app.
/// </summary>
internal static class Log
{
    private const int RingSize = 200;

    private static readonly LinkedList<string> _ring = new();
    private static readonly List<Action<string>> _listeners = new();
    private static readonly object _lock = new();
    private static readonly string LogPath =
        Path.Combine(ConfigManager.ConfigDir, "telemetry", "macro.log");

    static Log()
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!); } catch { }
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message) => Write("ERR ", message);
    public static void Debug(string message) => Write("DBG ", message);

    private static void Write(string level, string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] {level} {message}";

        List<Action<string>> snapshot;
        lock (_lock)
        {
            _ring.AddLast(line);
            while (_ring.Count > RingSize) _ring.RemoveFirst();
            snapshot = new List<Action<string>>(_listeners);
        }

        try { File.AppendAllText(LogPath, line + Environment.NewLine); }
        catch { }

        // Fire listeners outside the lock so a slow listener can't stall logging.
        foreach (var listener in snapshot)
        {
            try { listener(line); } catch { }
        }
    }

    /// <summary>
    /// Returns the most recent log lines (up to <paramref name="max"/>) for
    /// crash reports. Oldest first.
    /// </summary>
    public static string[] GetBreadcrumbs(int max = 50)
    {
        lock (_lock)
        {
            if (_ring.Count <= max) return _ring.ToArray();
            var skip = _ring.Count - max;
            var result = new string[max];
            int i = 0;
            foreach (var line in _ring)
            {
                if (skip-- > 0) continue;
                result[i++] = line;
            }
            return result;
        }
    }

    /// <summary>Subscribes to future log lines. Returns a disposable to unsubscribe.</summary>
    public static IDisposable Subscribe(Action<string> listener)
    {
        lock (_lock) _listeners.Add(listener);
        return new Subscription(listener);
    }

    private sealed class Subscription : IDisposable
    {
        private readonly Action<string> _listener;
        public Subscription(Action<string> listener) { _listener = listener; }
        public void Dispose()
        {
            lock (_lock) _listeners.Remove(_listener);
        }
    }
}
