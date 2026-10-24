using System.Diagnostics;
using System.ServiceProcess;
using System.Text.RegularExpressions;
using NetDeploy.Core.Steps;

namespace NetDeploy.Core;

/// <summary>One install found in an install folder: a subfolder, named after the install, that holds the app.</summary>
public sealed record Installation(string Name, string Directory, string? Version);

/// <summary>A Windows service that runs a program in an install, and its state; null when it can't be read.</summary>
public sealed record InstallService(string Name, ServiceControllerStatus? Status);

/// <summary>
/// Finds installs by looking at the folder, so installs made by hand count as much as ones NetDeploy made. Nothing is
/// saved to mark an install: the app's own files in a folder are what make it one.
/// </summary>
public static partial class Installations
{
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_-]{0,39}$")]
    private static partial Regex NamePattern();

    /// <summary>Install names end up in folder and service names, so they stay short and plain.</summary>
    public static string? CheckName(string name) =>
        NamePattern().IsMatch(name) ? null : "Use up to 40 letters, digits, - or _, starting with a letter or digit.";

    /// <summary>{Instance}, {InstanceSuffix} and {InstanceDir} for the install with this name.</summary>
    public static IReadOnlyDictionary<string, string> Variables(string installFolder, string name) => new Dictionary<string, string>
    {
        ["Instance"] = name,
        ["InstanceSuffix"] = "-" + name,
        ["InstanceDir"] = Path.Combine(installFolder, name),
    };

    /// <summary>
    /// The subfolders of the install folder that hold the app, by name. appFiles are file names that mark the app, such
    /// as its exe (see AppFiles). With none, any exe counts.
    /// </summary>
    public static IReadOnlyList<Installation> Find(string installFolder, IReadOnlyCollection<string> appFiles)
    {
        if (!Path.IsPathFullyQualified(installFolder) || !System.IO.Directory.Exists(installFolder))
            return [];

        return System.IO.Directory.EnumerateDirectories(installFolder)
            .Where(folder => HoldsApp(folder, appFiles))
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(folder => new Installation(Path.GetFileName(folder), folder, VersionOf(folder, appFiles)))
            .ToList();
    }

    /// <summary>
    /// The services whose program is in each folder, read from the services' command lines so services set up by hand
    /// count too. Every folder is in the result, with no services when none run from it.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Services(IEnumerable<string> folders)
    {
        var programs = WindowsServices.Programs();
        return folders.Distinct(StringComparer.OrdinalIgnoreCase).ToDictionary(
            folder => folder,
            folder => (IReadOnlyList<string>)programs.Where(service => WindowsServices.IsInside(service.Program, folder)).Select(service => service.Name).ToList(),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The services' states now.</summary>
    public static IReadOnlyList<InstallService> States(IEnumerable<string> services) =>
        services.Select(name => new InstallService(name, WindowsServices.Status(name))).ToList();

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
