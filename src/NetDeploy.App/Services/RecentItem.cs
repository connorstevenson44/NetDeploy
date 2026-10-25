namespace NetDeploy.App.Services;

/// <summary>Something opened before, for the start screen.</summary>
public sealed record RecentItem(string Path, RecentKind Kind, DateTime Opened);
