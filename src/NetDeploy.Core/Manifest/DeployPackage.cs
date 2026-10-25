using System.IO.Compression;

namespace NetDeploy.Core.Manifest;

/// <summary>An opened package: a zip extracted to a temp folder, or a folder used in place.</summary>
public sealed class DeployPackage : IDisposable
{
    public const string ManifestFileName = "deploy.json";

    private readonly bool _ownsFolder;

    private DeployPackage(string sourcePath, string rootPath, (DeployManifest Manifest, IReadOnlyList<string> Warnings) read, bool ownsFolder)
    {
        SourcePath = sourcePath;
        RootPath = rootPath;
        (Manifest, Warnings) = read;
        _ownsFolder = ownsFolder;
    }

    public string SourcePath { get; }
    public string RootPath { get; }
    public DeployManifest Manifest { get; }

    /// <summary>What was left out or looks off when the manifest was read, such as a setting from another version.</summary>
    public IReadOnlyList<string> Warnings { get; }

    /// <summary>Opens a .zip package, or a folder that holds deploy.json (handy while writing a manifest).</summary>
    public static DeployPackage Open(string path)
    {
        if (Directory.Exists(path))
            return new DeployPackage(path, Path.GetFullPath(path), ReadManifest(path), ownsFolder: false);

        var folder = Path.Combine(Path.GetTempPath(), "NetDeploy", Path.GetRandomFileName());
        try
        {
            // ExtractToDirectory refuses entries that would land outside the folder ("zip slip").
            ZipFile.ExtractToDirectory(path, folder);
            return new DeployPackage(path, folder, ReadManifest(folder), ownsFolder: true);
        }
        catch
        {
            // A zip that fails halfway through extracting leaves part of itself behind.
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
            throw;
        }
    }

    private static (DeployManifest, IReadOnlyList<string>) ReadManifest(string folder)
    {
        var manifestPath = Path.Combine(folder, ManifestFileName);
        if (!File.Exists(manifestPath))
            throw new ManifestException([$"The package has no {ManifestFileName} at its root."]);
        return ManifestLoader.ParseLeniently(File.ReadAllText(manifestPath));
    }

    public void Dispose()
    {
        if (!_ownsFolder)
            return;
        try
        {
            Directory.Delete(RootPath, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: a file still held open leaves the temp folder behind, which is harmless.
        }
    }
}
