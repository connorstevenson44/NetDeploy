namespace NetDeploy.Core.Config;

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
    public static MergePlan PlanNewFile(string incomingText, IConfigFormat format)
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
