namespace NetDeploy.App.ViewModels;

/// <summary>
/// A stretch of the file in the editor that differs from the server's file, or needs a choice, with the buttons that
/// act on it. Start and Length are where it is in the text being edited; Removed is the server's text it replaces.
/// </summary>
public sealed record ChangeMark(MarkKind Kind, int Start, int Length, string Removed, string Note, IReadOnlyList<MarkAction> Actions)
{
    public int End => Start + Length;
}
