using System.Text.Json;
using Microsoft.Win32;
using NetDeploy.Core.Deployment;
using NetDeploy.Core.Processes;

namespace NetDeploy.Core.Steps;

public sealed class PrerequisiteCheck
{
    public required PrerequisiteKind Kind { get; init; }

    /// <summary>dotnetRuntime: a shared framework such as Microsoft.NETCore.App or Microsoft.AspNetCore.App.</summary>
    public string? Framework { get; init; }

    /// <summary>dotnetRuntime and installedProgram: the lowest acceptable version.</summary>
    public string? MinVersion { get; init; }

    /// <summary>installedProgram: part of the name shown in Apps and Features.</summary>
    public string? Name { get; init; }

    /// <summary>registryValue: a key such as HKLM\SOFTWARE\Vendor\Product.</summary>
    public string? Key { get; init; }

    /// <summary>registryValue: a value under the key. When missing, the key existing is enough.</summary>
    public string? ValueName { get; init; }

    /// <summary>fileExists: a file or folder.</summary>
    public string? Path { get; init; }

    /// <summary>powershell: a script that outputs True or False.</summary>
    public string? Script { get; init; }

    public string Describe() => Kind switch
    {
        PrerequisiteKind.DotnetRuntime => $"{Framework} {MinVersion}",
        PrerequisiteKind.InstalledProgram => $"{Name} {MinVersion}".TrimEnd(),
        PrerequisiteKind.RegistryValue => $@"{Key}\{ValueName}".TrimEnd('\\'),
        PrerequisiteKind.FileExists => Path!,
        _ => "PowerShell check",
    };

    internal async Task<bool> IsSatisfiedAsync(StepContext context, CancellationToken cancellationToken) => Kind switch
    {
        PrerequisiteKind.DotnetRuntime => HasDotnetRuntime(Framework!, ParseVersion(MinVersion!)!),
        PrerequisiteKind.InstalledProgram => HasInstalledProgram(context.Resolve(Name!), MinVersion is null ? null : ParseVersion(MinVersion)),
        PrerequisiteKind.RegistryValue => HasRegistryValue(context.Resolve(Key!), ValueName),
        PrerequisiteKind.FileExists => File.Exists(context.Resolve(Path!)) || Directory.Exists(context.Resolve(Path!)),
        _ => await PowerShellRunner.EvaluateAsync(context.Resolve(Script!), context.Log, cancellationToken),
    };

    internal IEnumerable<string> Validate()
    {
        var missing = Kind switch
        {
            PrerequisiteKind.DotnetRuntime when Framework is null || MinVersion is null => "'framework' and 'minVersion'",
            PrerequisiteKind.InstalledProgram when Name is null => "'name'",
            PrerequisiteKind.RegistryValue when Key is null => "'key'",
            PrerequisiteKind.FileExists when Path is null => "'path'",
            PrerequisiteKind.PowerShell when Script is null => "'script'",
            _ => null,
        };
        if (missing is not null)
            yield return $"A {JsonNamingPolicy.CamelCase.ConvertName(Kind.ToString())} check needs {missing}.";
        if (MinVersion is not null && ParseVersion(MinVersion) is null)
            yield return $"'{MinVersion}' is not a version number.";
    }

    /// <summary>
    /// Looks for a runtime with the same major version, at or above the minimum.
    /// .NET apps don't roll forward to a new major version by default, so a newer major doesn't count.
    /// </summary>
    internal static bool HasDotnetRuntime(string framework, Version minimum, string? dotnetRoot = null)
    {
        dotnetRoot ??= System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet");
        var frameworkFolder = System.IO.Path.Combine(dotnetRoot, "shared", framework);
        return Directory.Exists(frameworkFolder)
            && Directory.GetDirectories(frameworkFolder)
                .Select(folder => ParseVersion(System.IO.Path.GetFileName(folder)))
                .Any(version => version is not null && version.Major == minimum.Major && version >= minimum);
    }

    private static bool HasInstalledProgram(string name, Version? minimum)
    {
        const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var uninstall = root.OpenSubKey(UninstallKey);
            foreach (var subKeyName in uninstall?.GetSubKeyNames() ?? [])
            {
                using var program = uninstall!.OpenSubKey(subKeyName);
                if (program?.GetValue("DisplayName") is not string displayName
                    || !displayName.Contains(name, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (minimum is null || ParseVersion(program.GetValue("DisplayVersion") as string ?? "") >= minimum)
                    return true;
            }
        }
        return false;
    }

    private static bool HasRegistryValue(string key, string? valueName)
    {
        var separator = key.IndexOf('\\');
        var hive = (separator < 0 ? key : key[..separator]).ToUpperInvariant() switch
        {
            "HKLM" or "HKEY_LOCAL_MACHINE" => RegistryHive.LocalMachine,
            "HKCU" or "HKEY_CURRENT_USER" => RegistryHive.CurrentUser,
            "HKCR" or "HKEY_CLASSES_ROOT" => RegistryHive.ClassesRoot,
            var other => throw new InvalidOperationException($"Unknown registry hive '{other}'."),
        };

        using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        using var subKey = separator < 0 ? root : root.OpenSubKey(key[(separator + 1)..]);
        return subKey is not null && (valueName is null || subKey.GetValue(valueName) is not null);
    }

    /// <summary>Parses "10.0.1" and also "10.0.0-preview.1", ignoring the suffix.</summary>
    internal static Version? ParseVersion(string text) =>
        Version.TryParse(text.Split('-', '+')[0], out var version) ? version : null;
}
