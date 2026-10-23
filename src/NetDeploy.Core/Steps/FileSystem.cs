namespace NetDeploy.Core.Steps;

internal static class FileSystem
{
    public static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)), overwrite: true);
    }

    public static void ReplaceDirectory(string source, string target)
    {
        if (Directory.Exists(target))
            Directory.Delete(target, recursive: true);
        CopyDirectory(source, target);
    }

    /// <summary>A relative path with forward slashes, the form glob patterns are written in.</summary>
    public static string RelativeUnixPath(string root, string path) =>
        Path.GetRelativePath(root, path).Replace('\\', '/');
}
