namespace NetDeploy.App.ViewModels;

/// <summary>How an install's Windows services are doing, as the dot beside it shows.</summary>
public enum ServiceState
{
    /// <summary>Not looked at yet: nothing shows.</summary>
    Unknown,

    /// <summary>No service runs a program in the install: a question mark shows.</summary>
    NotFound,

    Running,
    Stopped,

    /// <summary>Some running and some not, or starting, stopping or paused.</summary>
    Mixed,
}
