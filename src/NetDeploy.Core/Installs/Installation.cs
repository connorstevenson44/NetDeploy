namespace NetDeploy.Core.Installs;

/// <summary>One install found in an install folder: a subfolder, named after the install, that holds the app.</summary>
public sealed record Installation(string Name, string Directory, string? Version);
