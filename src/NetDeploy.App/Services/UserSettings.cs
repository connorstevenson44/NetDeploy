using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NetDeploy.App.Services;

public enum RecentKind { Package, Folder, Config }

/// <summary>Something opened before, for the start screen.</summary>
public sealed record RecentItem(string Path, RecentKind Kind, DateTime Opened);

/// <summary>
/// What NetDeploy remembers for the person using it: what they opened lately, and the values they last deployed each
/// app with. It lives in their own %LOCALAPPDATA%\NetDeploy, never beside the app or in an install folder, and holds no
/// secrets. Without a file (in tests) it remembers for as long as the app runs.
/// </summary>
public sealed class UserSettings
{
    internal const int MaxRecent = 8;

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        // Fills the lists and dictionaries Data makes, so they keep their case-insensitive keys.
        PreferredObjectCreationHandling = JsonObjectCreationHandling.Populate,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string? _path;
    private readonly Data _data;

    private UserSettings(string? path, Data data)
    {
        _path = path;
        _data = data;
    }

    /// <summary>%LOCALAPPDATA%\NetDeploy\settings.json.</summary>
    public static string DefaultPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetDeploy", "settings.json");

    /// <summary>Settings kept only while the app runs.</summary>
    public static UserSettings InMemory() => new(null, new Data());

    /// <summary>Reads the file. A file that is missing or can't be read starts the settings afresh rather than stopping the app.</summary>
    public static UserSettings Load(string path)
    {
        try
        {
            return new(path, JsonSerializer.Deserialize<Data>(File.ReadAllText(path), Options) ?? new Data());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new(path, new Data());
        }
    }

    /// <summary>What was opened lately, newest first.</summary>
    public IReadOnlyList<RecentItem> Recent => _data.Recent;

    /// <summary>Puts a path at the top of the recent list.</summary>
    public void AddRecent(string path, RecentKind kind)
    {
        var full = Path.GetFullPath(path);
        _data.Recent.RemoveAll(item => item.Path.Equals(full, StringComparison.OrdinalIgnoreCase));
        _data.Recent.Insert(0, new RecentItem(full, kind, DateTime.Now));
        if (_data.Recent.Count > MaxRecent)
            _data.Recent.RemoveRange(MaxRecent, _data.Recent.Count - MaxRecent);
        Save();
    }

    /// <summary>Takes a path off the recent list, such as one that no longer exists.</summary>
    public void RemoveRecent(string path)
    {
        if (_data.Recent.RemoveAll(item => item.Path.Equals(path, StringComparison.OrdinalIgnoreCase)) > 0)
            Save();
    }

    /// <summary>The install folder an app was last deployed to, or null.</summary>
    public string? LastFolder(string app) => _data.Apps.GetValueOrDefault(app)?.LastFolder;

    /// <summary>The values an app was last deployed with into this install folder ("" for an app without one).</summary>
    // A dictionary read back from the file compares names with case, so the copy is what lets them match without it.
    public IReadOnlyDictionary<string, string> Values(string app, string folder) =>
        new Dictionary<string, string>(
            _data.Apps.GetValueOrDefault(app)?.Folders.GetValueOrDefault(Key(folder)) ?? [], StringComparer.OrdinalIgnoreCase);

    /// <summary>Remembers the values an app is deployed with. The caller leaves secrets out.</summary>
    public void SaveValues(string app, string folder, IReadOnlyDictionary<string, string> values)
    {
        if (!_data.Apps.TryGetValue(app, out var saved))
            _data.Apps[app] = saved = new AppValues();
        saved.LastFolder = folder.Length == 0 ? null : folder;
        saved.Folders[Key(folder)] = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);
        Save();
    }

    // Folders are told apart as Windows does, without case or a trailing backslash.
    private static string Key(string folder) => Path.TrimEndingDirectorySeparator(folder).ToUpperInvariant();

    /// <summary>Writes the file. Settings are a convenience, so a failed write only loses them.</summary>
    private void Save()
    {
        if (_path is null)
            return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_data, Options));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed class Data
    {
        public List<RecentItem> Recent { get; } = [];
        public Dictionary<string, AppValues> Apps { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class AppValues
    {
        public string? LastFolder { get; set; }
        public Dictionary<string, Dictionary<string, string>> Folders { get; } = [];
    }
}
