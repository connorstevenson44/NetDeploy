using System.Text.RegularExpressions;

namespace NetDeploy.Core.Manifest;

/// <summary>Replaces {Name} placeholders with parameter and input values.</summary>
public static partial class Placeholders
{
    // Only {Identifier} is a placeholder, so PowerShell blocks such as { $_.Name } are left alone.
    [GeneratedRegex(@"\{([A-Za-z_][A-Za-z0-9_]*)\}")]
    private static partial Regex Pattern();

    /// <summary>Unknown names are left as they are, so a value can still be filled in later.</summary>
    public static string Resolve(string text, IReadOnlyDictionary<string, string> values) =>
        Pattern().Replace(text, match => values.TryGetValue(match.Groups[1].Value, out var value) ? value : match.Value);

    /// <summary>The names still left as {Name} in text.</summary>
    public static IReadOnlyList<string> Unresolved(string text) =>
        Pattern().Matches(text).Select(match => match.Groups[1].Value).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}
