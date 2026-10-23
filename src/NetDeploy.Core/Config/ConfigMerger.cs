namespace NetDeploy.Core.Config;

public enum MergeChoice { KeepCurrent, TakeIncoming, Edit, Remove }

public sealed record MergeOptions(IReadOnlyList<string> TakeNew, bool KeepRemoved);

/// <summary>One setting in the merge table.</summary>
public sealed class MergeRow(string path, string? baseValue, string? current, string? incoming, MergeChoice? choice, string reason)
{
    public string Path { get; } = path;

    /// <summary>The value in the package deployed last time, or null if it had none.</summary>
    public string? Base { get; } = baseValue;

    public string? Current { get; } = current;
    public string? Incoming { get; } = incoming;
    public string Reason { get; } = reason;
    public bool IsConflict { get; } = choice is null;

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
        _ => throw new InvalidOperationException($"'{Path}' changed on both sides and needs a choice."),
    };
}

public sealed class MergePlan(IConfigFormat format, string incomingText, IReadOnlyList<MergeRow> rows)
{
    public IConfigFormat Format { get; } = format;
    public IReadOnlyList<MergeRow> Rows { get; } = rows;

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

    /// <summary>The merged file: the incoming file with every row whose result differs from it applied.</summary>
    public string BuildResult() =>
        Format.Write(incomingText, Rows
            .Where(row => row.Result != row.Incoming)
            .Select(row => KeyValuePair.Create(row.Path, row.Result)));
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

        return new MergePlan(format, incomingText, rows);
    }

    private static (MergeChoice? Choice, string Reason) Propose(
        string path, OrderedDictionary<string, string>? baseValues, string? current, string? incoming, MergeOptions options)
    {
        if (current == incoming)
            return (MergeChoice.TakeIncoming, "Unchanged");

        if (options.TakeNew.Any(prefix => IsUnder(path, prefix)))
            return incoming is null ? (MergeChoice.Remove, "In takeNew") : (MergeChoice.TakeIncoming, "In takeNew");

        if (baseValues is null)
        {
            // First deploy through NetDeploy: no record of the old defaults, so treat differences as edits.
            if (current is null)
                return (MergeChoice.TakeIncoming, "New in the package");
            if (incoming is null)
                return Removed(options);
            return (MergeChoice.KeepCurrent, "Differs; no earlier package to compare with");
        }

        var original = baseValues.GetValueOrDefault(path);
        if (current == original)
        {
            if (incoming is null)
                return Removed(options);
            return (MergeChoice.TakeIncoming, original is null ? "New in the package" : "Changed in the package");
        }

        if (incoming == original)
            return (MergeChoice.KeepCurrent, current is null ? "Removed on the server" : "Changed on the server");

        return (null, "Changed on both sides");
    }

    private static (MergeChoice?, string) Removed(MergeOptions options) =>
        options.KeepRemoved
            ? (MergeChoice.KeepCurrent, "Removed from the package; kept")
            : (MergeChoice.Remove, "Removed from the package");

    private static bool IsUnder(string path, string prefix) =>
        path.Equals(prefix, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(prefix + ":", StringComparison.OrdinalIgnoreCase);
}
