namespace NetDeploy.Core.Config;

/// <summary>One setting in the merge table.</summary>
public sealed class MergeRow(string path, string? baseValue, string? current, string? incoming, MergeChoice? choice, string reason)
{
    public string Path { get; } = path;

    /// <summary>The value in the package deployed last time, or null if it had none.</summary>
    public string? Base { get; } = baseValue;

    public string? Current { get; private set; } = current;
    public string? Incoming { get; } = incoming;
    public string Reason { get; private set; } = reason;
    public bool IsConflict { get; private set; } = choice is null;

    /// <summary>False when the server and the package already agree.</summary>
    public bool IsChanged => Current != Incoming;

    /// <summary>Null until the user resolves a conflict.</summary>
    public MergeChoice? Choice { get; set; } = choice;

    /// <summary>The value used when Choice is Edit, in the format's canonical form.</summary>
    public string? EditedValue { get; set; }

    /// <summary>The value the merged file will have; null means the setting is left out.</summary>
    public string? Result => Choice switch
    {
        MergeChoice.KeepCurrent => Current,
        MergeChoice.TakeIncoming => Incoming,
        MergeChoice.Edit => EditedValue,
        MergeChoice.Remove => null,
        _ => throw new InvalidOperationException($"'{Path}' needs a choice: {Reason}"),
    };

    /// <summary>
    /// Gives a {{Name}} setting its value. In a new file, the token's default stands in for the value before, so the
    /// review shows what it changed to.
    /// </summary>
    internal void Fill(string value, string? before, string reason)
    {
        Current = before ?? Current;
        EditedValue = value;
        Choice = MergeChoice.Edit;
        Reason = reason;
    }

    /// <summary>Makes the user decide this row, for example because its value is still a placeholder.</summary>
    internal void RequireChoice(string reason)
    {
        Choice = null;
        IsConflict = true;
        Reason = reason;
    }

    /// <summary>
    /// Gives the row the value the user wrote in the file's text, null when they took the setting out. A value that is
    /// already on one side picks that side, so the row reads as kept or taken rather than edited.
    /// </summary>
    internal void Take(string? value, bool settled = false)
    {
        // A setting that needs a value keeps needing one until the text gives it another value, or the user settles it.
        if (value == Draft && !settled)
            return;
        if (value is null)
            Choice = MergeChoice.Remove;
        else if (value == Incoming)
            Choice = MergeChoice.TakeIncoming;
        else if (value == Current)
            Choice = MergeChoice.KeepCurrent;
        else
        {
            EditedValue = value;
            Choice = MergeChoice.Edit;
            Reason = "Your edit";
        }
    }

    /// <summary>The result, or while the row still needs a choice, the value it holds for now.</summary>
    internal string? Draft => Choice is null ? Incoming ?? Current : Result;

    internal (MergeChoice? Choice, string? EditedValue, string Reason) Save() => (Choice, EditedValue, Reason);

    internal void Restore((MergeChoice? Choice, string? EditedValue, string Reason) saved) => (Choice, EditedValue, Reason) = saved;
}
