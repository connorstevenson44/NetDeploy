namespace NetDeploy.Core.Config;

public enum MergeChoice { KeepCurrent, TakeIncoming, Edit, Remove }

public sealed record MergeOptions(IReadOnlyList<string> TakeNew);

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
    internal void Take(string? value)
    {
        // A setting that needs a value keeps needing one until the text gives it another value.
        if (value == Draft)
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

/// <summary>
/// The rows of one file and how to write it. The result starts from startText (the package's file, or the
/// server's file when startIsCurrent) and applies every row whose result differs from that side. Once the user edits
/// the whole file as text, the result starts from their text instead.
/// </summary>
public sealed class MergePlan(IConfigFormat format, string startText, IReadOnlyList<MergeRow> rows, bool startIsCurrent = false, string? currentText = null)
{
    private readonly List<MergeRow> _rows = [.. rows];
    private string? _editedText;
    private OrderedDictionary<string, string>? _editedValues;

    // The settings and text as they were when the user started editing the text.
    private (List<(MergeRow Row, (MergeChoice?, string?, string) Saved)> Rows, string? Text, OrderedDictionary<string, string>? Values)? _beforeText;

    public IConfigFormat Format { get; } = format;
    public IReadOnlyList<MergeRow> Rows => _rows;

    /// <summary>The server's file as it is now, or null when the server has none.</summary>
    public string? CurrentText { get; } = currentText ?? (startIsCurrent ? startText : null);

    /// <summary>The whole file as the user last wrote it, or null when they haven't.</summary>
    public string? EditedText => _editedText;

    public int ConflictCount => Rows.Count(row => row.Choice is null);

    public string Summary
    {
        get
        {
            var changed = Rows.Where(row => row.IsChanged).ToList();
            int Count(MergeChoice choice) => changed.Count(row => row.Choice == choice);
            return $"{changed.Count} settings differ: {Count(MergeChoice.KeepCurrent)} kept from the server, "
                + $"{Count(MergeChoice.TakeIncoming)} taken from the package, {Count(MergeChoice.Remove)} removed, "
                + $"{ConflictCount} to resolve.";
        }
    }

    /// <summary>The merged file. With nothing to change, the start file is returned as it is, keeping its layout.</summary>
    public string BuildResult() => Build(row => row.Result);

    /// <summary>
    /// The merged file to edit as text. Settings that still need a value show what they hold for now, where
    /// BuildResult would throw.
    /// </summary>
    public string BuildText() => Build(row => row.Draft);

    /// <summary>True when writing the result would change the file the plan starts from.</summary>
    public bool ChangesStartFile => _editedText is not null ? BuildResult() != startText : Changes(row => row.Result).Any();

    /// <summary>
    /// Starts editing the whole file as text. Each Edit after it applies its text to the settings as they are now, so
    /// text typed and then taken back leaves them as they were.
    /// </summary>
    public void StartTextEdit() =>
        _beforeText = (_rows.Select(row => (row, row.Save())).ToList(), _editedText, _editedValues);

    /// <summary>
    /// Takes the whole file as the user wrote it: it becomes the result, and every setting follows it, so the diff
    /// shows what the text changes. Settings it adds become rows of their own. Throws when the text can't be read,
    /// and then changes nothing.
    /// </summary>
    public void Edit(string text)
    {
        var values = Format.Read(text);
        if (_beforeText is null)
            StartTextEdit();
        var (rows, _, _) = _beforeText!.Value;
        _rows.Clear();
        foreach (var (row, saved) in rows)
        {
            row.Restore(saved);
            row.Take(values.GetValueOrDefault(row.Path));
            _rows.Add(row);
        }
        // A setting the text adds goes after the one written before it, so the diff keeps the text's order.
        var known = _rows.Select(row => row.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string? previous = null;
        foreach (var (path, value) in values)
        {
            if (!known.Contains(path))
            {
                var at = _rows.FindIndex(row => string.Equals(row.Path, previous, StringComparison.OrdinalIgnoreCase)) + 1;
                _rows.Insert(at, new MergeRow(path, null, null, null, MergeChoice.Edit, "Your edit") { EditedValue = value });
            }
            previous = path;
        }
        _editedText = text;
        _editedValues = values;
    }

    /// <summary>Puts the settings and the text back as they were before StartTextEdit.</summary>
    public void CancelTextEdit()
    {
        if (_beforeText is not { } before)
            return;
        _rows.Clear();
        foreach (var (row, saved) in before.Rows)
        {
            row.Restore(saved);
            _rows.Add(row);
        }
        (_editedText, _editedValues) = (before.Text, before.Values);
    }

    private string Build(Func<MergeRow, string?> value)
    {
        var changes = Changes(value).ToList();
        var start = _editedText ?? startText;
        return changes.Count == 0 ? start : Format.Write(start, changes);
    }

    // Rows whose value isn't already in the file the result starts from.
    private IEnumerable<KeyValuePair<string, string?>> Changes(Func<MergeRow, string?> value) => Rows
        .Select(row => (row.Path, Value: value(row), Start: StartValue(row)))
        .Where(row => row.Value != row.Start)
        .Select(row => KeyValuePair.Create(row.Path, row.Value));

    private string? StartValue(MergeRow row) =>
        _editedValues is not null ? _editedValues.GetValueOrDefault(row.Path) : startIsCurrent ? row.Current : row.Incoming;
}

/// <summary>
/// A three-way merge of settings: base (the package deployed last time), current (the server now)
/// and incoming (the new package). The new file decides the shape; the server keeps its edits.
/// </summary>
public static class ConfigMerger
{
    public static MergePlan Plan(string? baseText, string currentText, string incomingText, IConfigFormat format, MergeOptions options)
    {
        var baseValues = baseText is null ? null : format.Read(baseText);
        var current = format.Read(currentText);
        var incoming = format.Read(incomingText);

        var rows = incoming.Keys.Concat(current.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path =>
            {
                var currentValue = current.GetValueOrDefault(path);
                var incomingValue = incoming.GetValueOrDefault(path);
                var (choice, reason) = Propose(path, baseValues, currentValue, incomingValue, options);
                return new MergeRow(path, baseValues?.GetValueOrDefault(path), currentValue, incomingValue, choice, reason);
            })
            .ToList();

        return new MergePlan(format, incomingText, rows, currentText: currentText);
    }

    /// <summary>A file the server doesn't have yet: every setting is new, taken from the package.</summary>
    public static MergePlan PlanNew(string incomingText, IConfigFormat format)
    {
        var rows = format.Read(incomingText)
            .Select(pair => new MergeRow(pair.Key, null, null, pair.Value, MergeChoice.TakeIncoming, "New file"))
            .ToList();
        return new MergePlan(format, incomingText, rows);
    }

    private static (MergeChoice? Choice, string Reason) Propose(
        string path, OrderedDictionary<string, string>? baseValues, string? current, string? incoming, MergeOptions options)
    {
        if (current == incoming)
            return (MergeChoice.TakeIncoming, "Unchanged");

        if (options.TakeNew.Any(prefix => IsUnder(path, prefix)))
        {
            const string AlwaysNew = "Always taken from the package (takeNew)";
            return incoming is null ? (MergeChoice.Remove, AlwaysNew) : (MergeChoice.TakeIncoming, AlwaysNew);
        }

        if (baseValues is null)
        {
            // First deploy through NetDeploy: no record of the old defaults, so treat differences as edits.
            if (current is null)
                return (MergeChoice.TakeIncoming, "New in the package");
            if (incoming is null)
                return Removed();
            return (MergeChoice.KeepCurrent, "Different on the server, and no earlier package to compare with");
        }

        var original = baseValues.GetValueOrDefault(path);
        if (current == original)
        {
            if (incoming is null)
                return Removed();
            return (MergeChoice.TakeIncoming, original is null ? "New in the package" : "Changed in the package");
        }

        if (incoming == original)
            return (MergeChoice.KeepCurrent, current is null ? "Removed on the server" : "Changed on the server");

        return (null, "Changed on both sides");
    }

    private static (MergeChoice?, string) Removed() => (MergeChoice.Remove, "Removed from the package");

    private static bool IsUnder(string path, string prefix) =>
        path.Equals(prefix, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(prefix + ":", StringComparison.OrdinalIgnoreCase);
}
