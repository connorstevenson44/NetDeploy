namespace NetDeploy.App.ViewModels;

/// <summary>What a mark in the editor stands for.</summary>
public enum MarkKind
{
    /// <summary>Text that changes from the server's file: what was there shows struck out, what replaces it in green.</summary>
    Change,

    /// <summary>A setting that needs the user to pick its value, highlighted until they do.</summary>
    NeedsChoice,

    /// <summary>Text kept from the server's file where the package has something else, which can be taken instead.</summary>
    PackageDiffers,
}
