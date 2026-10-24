using System.Diagnostics;
using System.Text.RegularExpressions;
using NetDeploy.Core.Steps;

namespace NetDeploy.Core;

/// <summary>One install found in an install folder: the single install in the folder itself, or a named instance.</summary>
public sealed record Installation(string Name, string Directory, string? Version)
{
    public bool IsInstance => Name.Length > 0;
}

/// <summary>
/// Finds installs by looking at the folder, so installs made by hand count as much as ones NetDeploy made. Nothing is
/// saved to mark an install: the app's own files in a folder are what make it one.
/// </summary>
public static partial class Installations
{
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_-]{0,39}$")]
    private static partial Regex NamePattern();

    /// <summary>Instance names end up in folder and service names, so they stay short and plain.</summary>
    public static string? CheckName(string name) =>
        NamePattern().IsMatch(name) ? null : "Use up to 40 letters, digits, - or _, starting with a letter or digit.";

    /// <summary>{Instance}, {InstanceSuffix} and {InstanceDir} for an instance; an empty name is the single install.</summary>
    public static IReadOnlyDictionary<string, string> Variables(string installFolder, string instance) => new Dictionary<string, string>
    {
        ["Instance"] = instance,
        ["InstanceSuffix"] = instance.Length == 0 ? "" : "-" + instance,
        ["InstanceDir"] = instance.Length == 0 ? installFolder : Path.Combine(installFolder, instance),
    };

    /// <summary>
    /// The single install comes first when the folder itself holds the app; instances are subfolders that hold it.
    /// appFiles are file names that mark the app, such as its exe (see AppFiles). With none, any exe counts.
    /// </summary>
    public static IReadOnlyList<Installation> Find(string installFolder, IReadOnlyCollection<string> appFiles)
    {
        if (!Path.IsPathFullyQualified(installFolder) || !System.IO.Directory.Exists(installFolder))
            return [];

        var found = new List<Installation>();
        if (HoldsApp(installFolder, appFiles))
            found.Add(new Installation("", installFolder, VersionOf(installFolder, appFiles)));
        foreach (var folder in InstanceFolders(installFolder, appFiles).Order(StringComparer.OrdinalIgnoreCase))
            found.Add(new Installation(Path.GetFileName(folder), folder, VersionOf(folder, appFiles)));
        return found;
    }

    /// <summary>
    /// The files at the top of the folder a package installs into {InstanceDir}: its exes, or its dlls when it has no
    /// exe, such as a web app.
    /// </summary>
    public static IReadOnlyList<string> AppFiles(DeployPackage package)
    {
        var install = package.Manifest.Install;
        var copies = Flatten(package.Manifest.Steps).OfType<CopyFilesStep>()
            .Where(copy => copy.To.Contains("{InstanceDir}", StringComparison.OrdinalIgnoreCase)
                || install is not null && copy.To.Equals(install.Folder, StringComparison.OrdinalIgnoreCase));

        var files = new List<string>();
        foreach (var copy in copies)
        {
            if (Placeholders.Unresolved(copy.From).Count > 0)
                continue;
            var source = StepContext.InsidePackage(package.RootPath, copy.From);
            if (System.IO.Directory.Exists(source))
                files.AddRange(TopAppFiles(source));
        }
        return files.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>The exes at the top of a folder, or its dlls when it has no exe.</summary>
    internal static IReadOnlyList<string> TopAppFiles(string folder)
    {
        var top = System.IO.Directory.EnumerateFiles(folder).Select(Path.GetFileName).OfType<string>().ToList();
        var exes = top.Where(name => name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)).ToList();
        return exes.Count > 0 ? exes : top.Where(name => name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>
    /// Subfolders of an install that hold one of its app files: other installs, such as the instances inside a single
    /// install, with their backups beside them. Copies and backups of the install leave them alone.
    /// </summary>
    internal static IReadOnlyList<string> NestedInstalls(string folder, IReadOnlyCollection<string> appFiles) =>
        appFiles.Count == 0 || !System.IO.Directory.Exists(folder)
            ? []
            : InstanceFolders(folder, appFiles).SelectMany(nested => new[] { nested, BackupStep.BackupsOf(nested) }).Where(System.IO.Directory.Exists).ToList();

    /// <summary>
    /// The files that belong to an install, relative to its folder: everything except instances nested in it and the
    /// folders in skipFolders.
    /// </summary>
    public static IEnumerable<string> OwnFiles(string installDirectory, IReadOnlyCollection<string> appFiles, params string[] skipFolders)
    {
        var skip = InstanceFolders(installDirectory, appFiles).Concat(skipFolders).Select(Path.GetFullPath).ToList();
        return System.IO.Directory.EnumerateFiles(installDirectory, "*", SearchOption.AllDirectories)
            .Where(file => !skip.Any(folder => file.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
            .Select(file => Path.GetRelativePath(installDirectory, file));
    }

    internal static IEnumerable<string> InstanceFolders(string installFolder, IReadOnlyCollection<string> appFiles) =>
        System.IO.Directory.EnumerateDirectories(installFolder).Where(folder => HoldsApp(folder, appFiles));

    /// <summary>Every step, with the steps inside if steps.</summary>
    internal static IEnumerable<DeployStep> Flatten(IEnumerable<DeployStep> steps)
    {
        foreach (var step in steps)
        {
            yield return step;
            if (step is IfStep ifStep)
                foreach (var nested in Flatten(ifStep.Then.Concat(ifStep.Else)))
                    yield return nested;
        }
    }

    private static bool HoldsApp(string folder, IReadOnlyCollection<string> appFiles) =>
        appFiles.Count > 0
            ? appFiles.Any(name => File.Exists(Path.Combine(folder, name)))
            : System.IO.Directory.EnumerateFiles(folder, "*.exe").Any();

    /// <summary>The product version of the app's first exe or dll, read from the file itself.</summary>
    private static string? VersionOf(string folder, IReadOnlyCollection<string> appFiles)
    {
        var file = appFiles.Select(name => Path.Combine(folder, name)).FirstOrDefault(File.Exists)
            ?? System.IO.Directory.EnumerateFiles(folder, "*.exe").FirstOrDefault();
        var version = file is null ? null : FileVersionInfo.GetVersionInfo(file).ProductVersion;
        // .NET adds the commit after a '+', which is noise here.
        return string.IsNullOrEmpty(version) ? null : version.Split('+')[0];
    }
}
