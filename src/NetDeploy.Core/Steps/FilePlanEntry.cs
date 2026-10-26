namespace NetDeploy.Core.Steps;

/// <summary>One file a copy step will look at, and what it will do with it.</summary>
public sealed class FilePlanEntry
{
    internal FilePlanEntry(string path, FileState state, bool isConfig, FileAction defaultAction)
    {
        Path = path;
        State = state;
        IsConfig = isConfig;
        DefaultAction = defaultAction;
        Action = defaultAction;
        Choices = (state, isConfig) switch
        {
            (FileState.OnlyOnServer, _) => [FileAction.Keep, FileAction.Delete],
            (FileState.New, true) => [FileAction.Merge, FileAction.Keep],
            (FileState.New, false) => [FileAction.Replace, FileAction.Keep],
            (_, true) => [FileAction.Merge, FileAction.Replace, FileAction.Keep, FileAction.Delete],
            _ => [FileAction.Replace, FileAction.Keep, FileAction.Delete],
        };
    }

    /// <summary>Relative to the copy's folders, with forward slashes.</summary>
    public string Path { get; }

    public FileState State { get; }
    public bool IsConfig { get; }
    public FileAction DefaultAction { get; }
    public IReadOnlyList<FileAction> Choices { get; }

    /// <summary>What the copy will do. Starts as DefaultAction; the file view lets the user change it.</summary>
    public FileAction Action { get; set; }
}
