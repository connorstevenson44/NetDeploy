namespace NetDeploy.App.ViewModels;

/// <summary>A copy step whose files can't be listed yet, with the reason.</summary>
public sealed record FilePlanError(string Title, string Message);
