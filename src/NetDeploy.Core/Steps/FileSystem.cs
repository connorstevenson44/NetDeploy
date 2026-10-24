namespace NetDeploy.Core.Steps;

internal static class FileSystem
{
    /// <summary>Copies everything in source except the skip folders, which are full paths of folders inside it.</summary>
    public static void CopyDirectory(string source, string target, IReadOnlyCollection<string>? skip = null)
    {
        Directory.CreateDirectory(target);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories).Where(path => !IsUnder(path, skip)))
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).Where(path => !IsUnder(path, skip)))
            File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)), overwrite: true);
    }

    /// <summary>Makes target a copy of source, leaving the keep folders (full paths inside target) as they are.</summary>
    public static void ReplaceDirectory(string source, string target, IReadOnlyCollection<string>? keep = null)
    {
        if (Directory.Exists(target))
        {
            foreach (var file in Directory.EnumerateFiles(target))
                File.Delete(file);
            foreach (var directory in Directory.EnumerateDirectories(target).Where(path => !IsUnder(path, keep)))
                Directory.Delete(directory, recursive: true);
        }
        CopyDirectory(source, target);
    }

    private static bool IsUnder(string path, IReadOnlyCollection<string>? folders) =>
        folders is not null && folders.Any(folder =>
            path.Equals(folder, StringComparison.OrdinalIgnoreCase) || path.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));

    /// <summary>True when two files have the same bytes. Compares sizes first, then reads both in chunks.</summary>
    public static bool SameContents(string first, string second)
    {
        if (new FileInfo(first).Length != new FileInfo(second).Length)
            return false;

        using var a = File.OpenRead(first);
        using var b = File.OpenRead(second);
        var bufferA = new byte[81920];
        var bufferB = new byte[81920];
        int read;
        while ((read = a.ReadAtLeast(bufferA, bufferA.Length, throwOnEndOfStream: false)) > 0)
        {
            b.ReadExactly(bufferB, 0, read);
            if (!bufferA.AsSpan(0, read).SequenceEqual(bufferB.AsSpan(0, read)))
                return false;
        }
        return true;
    }

    /// <summary>A relative path with forward slashes, the form glob patterns are written in.</summary>
    public static string RelativeUnixPath(string root, string path) =>
        Path.GetRelativePath(root, path).Replace('\\', '/');
}
