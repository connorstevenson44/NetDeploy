namespace NetDeploy.Core.Steps;

/// <summary>How a file in the package compares with the server.</summary>
public enum FileState
{
    /// <summary>Only in the package.</summary>
    New,

    /// <summary>In both, with different contents.</summary>
    Changed,

    /// <summary>In both, identical.</summary>
    Same,

    /// <summary>Only on the server.</summary>
    OnlyOnServer,
}

/// <summary>What the copy does with a file.</summary>
public enum FileAction
{
    /// <summary>Copy the package's file, adding or overwriting the server's.</summary>
    Replace,

    /// <summary>Leave the server as it is: an existing file stays, a new one is not added.</summary>
    Keep,

    /// <summary>Delete the server's file.</summary>
    Delete,

    /// <summary>Merge a config file with the server's copy, reviewed before it is written.</summary>
    Merge,
}

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

/// <summary>Every file a copy step will look at: the package's files and the ones only on the server.</summary>
public sealed record FilePlan(string Source, string Target, IReadOnlyList<FilePlanEntry> Entries);
