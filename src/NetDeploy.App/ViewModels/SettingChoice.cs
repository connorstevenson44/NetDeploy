namespace NetDeploy.App.ViewModels;

/// <summary>A setting that needs a value, listed above the editor with the ways to settle it.</summary>
public sealed record SettingChoice(string Path, string Reason, IReadOnlyList<MarkAction> Actions);
