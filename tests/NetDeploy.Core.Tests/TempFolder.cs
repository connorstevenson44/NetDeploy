namespace NetDeploy.Core.Tests;

internal sealed class TempFolder : IDisposable
{
    public string Path { get; } = Directory.CreateDirectory(
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "NetDeployTests", System.IO.Path.GetRandomFileName())).FullName;

    public string File(string relativePath, string content)
    {
        var full = Combine(relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        System.IO.File.WriteAllText(full, content);
        return full;
    }

    public string Read(string relativePath) => System.IO.File.ReadAllText(Combine(relativePath));

    public bool Exists(string relativePath) => System.IO.File.Exists(Combine(relativePath));

    public string Combine(string relativePath) => System.IO.Path.Combine(Path, relativePath);

    public void Dispose() => Directory.Delete(Path, recursive: true);
}
