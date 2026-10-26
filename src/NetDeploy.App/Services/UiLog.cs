using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using NetDeploy.Core.Deployment;

namespace NetDeploy.App.Services;

/// <summary>
/// Writes the deploy log to the window and to a file in the logs folder. The window gets the lines in batches and
/// keeps the newest few thousand, so a tool that prints fast can't flood the UI thread or fill memory; the file has
/// every line.
/// </summary>
internal sealed class UiLog : IDeployLog, IDisposable
{
    internal const int MaxShown = 5000;

    private readonly ObservableCollection<LogEntry> _entries;
    private readonly StreamWriter _file;
    private readonly Lock _gate = new();
    private readonly ConcurrentQueue<LogEntry> _pending = new();
    private int _flushQueued;

    private UiLog(ObservableCollection<LogEntry> entries, StreamWriter file)
    {
        _entries = entries;
        _file = file;
    }

    public string FilePath => LogFiles.PathOf(_file);

    public static UiLog Create(ObservableCollection<LogEntry> entries, string appName, string kind) => new(entries, LogFiles.Create(appName, kind));

    /// <summary>Saves an unexpected error's full details and returns the file's path.</summary>
    public static string WriteCrashReport(Exception exception)
    {
        using var file = LogFiles.Create($"crash-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        file.Write(exception.ToString());
        return LogFiles.PathOf(file);
    }

    public void Write(LogLevel level, string message)
    {
        var entry = new LogEntry(DateTime.Now, level, message);
        lock (_gate)
            _file.WriteLine(entry);
        _pending.Enqueue(entry);
        if (Interlocked.Exchange(ref _flushQueued, 1) == 0)
            Application.Current.Dispatcher.InvokeAsync(Flush, DispatcherPriority.Background);
    }

    /// <summary>Moves the lines written since the last flush to the window, dropping the oldest past MaxShown.</summary>
    private void Flush()
    {
        // Cleared first, so a line written while this runs queues the next flush rather than being missed.
        Volatile.Write(ref _flushQueued, 0);
        while (_pending.TryDequeue(out var entry))
            _entries.Add(entry);
        while (_entries.Count > MaxShown)
            _entries.RemoveAt(0);
    }

    public void Dispose()
    {
        lock (_gate)
            _file.Dispose();
    }
}
