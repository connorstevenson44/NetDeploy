using MaterialDesignThemes.Wpf;
using NetDeploy.Core.Installs;

namespace NetDeploy.App.ViewModels;

/// <summary>A row in the install picker: an install found in the folder, or a new one.</summary>
public sealed record InstallTarget(Installation? Install)
{
    public bool IsNew => Install is null;
    public string Label => Install?.Name ?? "New";

    /// <summary>The label in the "start from a copy of" list, where no install means a fresh one.</summary>
    public string CopyLabel => Install is null ? "Nothing, a clean install" : $"A copy of {Label}";

    public string Detail => Install?.Version is { } version ? "v" + version : "";

    public PackIconKind Icon => Install is null ? PackIconKind.PlusCircleOutline : PackIconKind.Server;
}
