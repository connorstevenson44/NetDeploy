// Builds the four test packages into dist\: dotnet run build.cs
// Each package is its folder in versions\ with the apps from src\ built into it. The config files in versions\ win
// over the ones the build makes, so each version's .exe.config is exactly what is checked in.
using System.Diagnostics;
using System.IO.Compression;

var root = AppContext.GetData("EntryPointFileDirectoryPath") as string ?? Directory.GetCurrentDirectory();
var dist = Path.Combine(root, "dist");
var work = Path.Combine(root, "obj", "packages");
Directory.CreateDirectory(dist);

foreach (var version in new[] { "1.0.0", "1.1.0" })
{
    var app = Stage($"contoso-orders-{version}");
    Dotnet($"publish src/Contoso.Orders -c Release -p:Version={version} -o \"{app}\"");
    Build("src/Contoso.Orders.Export", version, Path.Combine(app, "export"), "Contoso.Orders.Export.exe");
    Pack($"contoso-orders-{version}", $"Contoso.Orders-{version}.zip");
}

foreach (var (version, plugins) in new[] { ("2.0.0", new[] { "Csv", "Pdf" }), ("2.1.0", ["Csv", "Excel"]) })
{
    var app = Stage($"fabrikam-reports-{version}");
    Build("src/Fabrikam.Reports", version, app, "Fabrikam.Reports.exe");
    foreach (var plugin in plugins)
    {
        var format = plugin == "Excel" ? "xlsx" : plugin.ToLowerInvariant();
        Build("src/Fabrikam.Reports.Plugin", version, Path.Combine(app, "plugins"), $"Fabrikam.Reports.{plugin}.dll",
            $"-p:AssemblyName=Fabrikam.Reports.{plugin} -p:Format={format} --no-incremental");
    }
    Pack($"fabrikam-reports-{version}", $"Fabrikam.Reports-{version}.zip");
}

Console.WriteLine($"Packages are in {dist}");

// A clean copy of the package folder; returns its app folder.
string Stage(string package)
{
    var folder = Path.Combine(work, package);
    if (Directory.Exists(folder))
        Directory.Delete(folder, recursive: true);
    return Directory.CreateDirectory(Path.Combine(folder, "app")).FullName;
}

// Builds a project and copies only the named file, so the build's own config files stay out of the package.
void Build(string project, string version, string target, string file, string extra = "")
{
    var output = Path.Combine(work, "build", Path.GetFileNameWithoutExtension(file));
    Dotnet($"build {project} -c Release -p:Version={version} {extra} -o \"{output}\"");
    Directory.CreateDirectory(target);
    File.Copy(Path.Combine(output, file), Path.Combine(target, file), overwrite: true);
}

// Lays the package folder over the built apps and zips the result.
void Pack(string package, string zipName)
{
    var source = Path.Combine(root, "versions", package);
    var staged = Path.Combine(work, package);
    foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
    {
        var copy = Path.Combine(staged, Path.GetRelativePath(source, file));
        Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
        File.Copy(file, copy, overwrite: true);
    }
    var zip = Path.Combine(dist, zipName);
    File.Delete(zip);
    ZipFile.CreateFromDirectory(staged, zip);
    Console.WriteLine($"Made {zip}");
}

void Dotnet(string arguments)
{
    Console.WriteLine($"> dotnet {arguments}");
    using var process = Process.Start(new ProcessStartInfo("dotnet", arguments) { WorkingDirectory = root })!;
    process.WaitForExit();
    if (process.ExitCode != 0)
        throw new InvalidOperationException($"dotnet {arguments} failed with exit code {process.ExitCode}.");
}
