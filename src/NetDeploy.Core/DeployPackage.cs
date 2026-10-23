using System.IO.Compression;

namespace NetDeploy.Core;

/// <summary>An opened package: a zip extracted to a temp folder, or a folder used in place.</summary>
public sealed class DeployPackage : IDisposable
{
    public const string ManifestFileName = "deploy.json";

    private readonly bool _ownsFolder;

    private DeployPackage(string sourcePath, string rootPath, DeployManifest manifest, bool ownsFolder)
    {
        SourcePath = sourcePath;
        RootPath = rootPath;
        Manifest = manifest;
        _ownsFolder = ownsFolder;
    }

    public string SourcePath { get; }
    public string RootPath { get; }
    public DeployManifest Manifest { get; }

    /// <summary>Opens a .zip package, or a folder that holds deploy.json (handy while writing a manifest).</summary>
    public static DeployPackage Open(string path)
    {
        if (Directory.Exists(path))
            return new DeployPackage(path, Path.GetFullPath(path), ReadManifest(path), ownsFolder: false);

        var folder = Path.Combine(Path.GetTempPath(), "NetDeploy", Path.GetRandomFileName());
        // ExtractToDirectory refuses entries that would land outside the folder ("zip slip").
        ZipFile.ExtractToDirectory(path, folder);
        try
        {
            return new DeployPackage(path, folder, ReadManifest(folder), ownsFolder: true);
        }
        catch
        {
            Directory.Delete(folder, recursive: true);
            throw;
        }
    }

    private static DeployManifest ReadManifest(string folder)
    {
        var manifestPath = Path.Combine(folder, ManifestFileName);
        if (!File.Exists(manifestPath))
            throw new ManifestException([$"The package has no {ManifestFileName} at its root."]);
        return ManifestLoader.Parse(File.ReadAllText(manifestPath));
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
