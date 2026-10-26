namespace NetDeploy.Core.Deployment;

/// <summary>The log files deploys write, from the window and from the command line alike.</summary>
public static class LogFiles
{
    /// <summary>A log named after the app, the time and what it records, such as "deploy" or "rollback".</summary>
    public static StreamWriter Create(string appName, string kind) => Create($"{appName}-{DateTime.Now:yyyyMMdd-HHmmss}-{kind}.log");

    /// <summary>Logs go next to the exe so they travel with it, or to LocalAppData if that folder is read-only.</summary>
    public static StreamWriter Create(string fileName)
    {
        try
        {
            return new StreamWriter(LogPath(AppContext.BaseDirectory, fileName)) { AutoFlush = true };
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            var localData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetDeploy");
            return new StreamWriter(LogPath(localData, fileName)) { AutoFlush = true };
        }
    }

    /// <summary>One line of a log, as the files and the command line show it.</summary>
    public static string Line(DateTime time, LogLevel level, string message) => $"{time:HH:mm:ss} {level,-7} {message}";

    /// <summary>The path of the file a writer from Create writes to.</summary>
    public static string PathOf(StreamWriter file) => ((FileStream)file.BaseStream).Name;

    private static string LogPath(string root, string fileName)
    {
        var folder = Directory.CreateDirectory(Path.Combine(root, "logs")).FullName;
        return Path.Combine(folder, fileName);
    }
}
