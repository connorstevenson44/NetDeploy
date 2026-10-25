namespace NetDeploy.Core.Config;

public enum ConfigFileKind
{
    /// <summary>The package's file merged with the server's copy.</summary>
    Update,

    /// <summary>The server has no copy yet, so the package's file is used as is.</summary>
    New,

    /// <summary>A server-only override, such as appsettings.Production.json, checked against the new appsettings.json.</summary>
    Override,

    /// <summary>A file on the server opened for editing, outside any deploy.</summary>
    Edit,
}
