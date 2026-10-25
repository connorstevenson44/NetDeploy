namespace NetDeploy.Core.Steps;

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
