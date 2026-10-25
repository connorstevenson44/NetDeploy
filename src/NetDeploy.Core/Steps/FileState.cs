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
