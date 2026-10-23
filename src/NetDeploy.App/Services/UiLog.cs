using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using NetDeploy.Core;

namespace NetDeploy.App.Services;

public sealed record LogEntry(DateTime Time, LogLevel Level, string Message)
{
    public override string ToString() => $"{Time:HH:mm:ss} {Level,-7} {Message}";
}

/// <summary>Writes the deploy log to the window and to a file in the logs folder.</summary>
internal sealed class UiLog : IDeployLog, IDisposable
{
    private readonly ObservableCollection<LogEntry> _entries;
    private readonly StreamWriter _file;
    private readonly Lock _gate = new();

    private UiLog(ObservableCollection<LogEntry> entries, string filePath)
    {
        _entries = entries;
        FilePath = filePath;
        _file = new StreamWriter(filePath) { AutoFlush = true };
    }

    public string FilePath { get; }

    /// <summary>Logs go next to the exe so they travel with it, or to LocalAppData if that folder is read-only.</summary>
    public static UiLog Create(ObservableCollection<LogEntry> entries, string appName, string kind)
    {
        var fileName = $"{appName}-{DateTime.Now:yyyyMMdd-HHmmss}-{kind}.log";
        try
        {
            return new UiLog(entries, LogPath(AppContext.BaseDirectory, fileName));
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            var localData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetDeploy");
            return new UiLog(entries, LogPath(localData, fileName));
        }
    }

    public void Write(LogLevel level, string message)
    {
        var entry = new LogEntry(DateTime.Now, level, message);
        lock (_gate)
            _file.WriteLine(entry);
        Application.Current.Dispatcher.InvokeAsync(() => _entries.Add(entry));
    }

    public void Dispose()
    {
        lock (_gate)
            _file.Dispose();
    }

    private static string LogPath(string root, string fileName)
    {
        var folder = Directory.CreateDirectory(Path.Combine(root, "logs")).FullName;
        return Path.Combine(folder, fileName);
    }
}
