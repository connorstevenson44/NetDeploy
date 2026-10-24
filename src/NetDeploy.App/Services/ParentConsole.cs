using System.Runtime.InteropServices;

namespace NetDeploy.App.Services;

/// <summary>
/// Lets the windowed exe write to the console it was started from. Output that is already going somewhere, such as
/// a pipe in a build pipeline, is left going there.
/// </summary>
internal static class ParentConsole
{
    private const int StdOutput = -11;
    private const int AttachParentProcess = -1;
    private const int FileTypeUnknown = 0;

    public static void Attach()
    {
        var output = GetStdHandle(StdOutput);
        if (output == 0 || output == -1 || GetFileType(output) == FileTypeUnknown)
            AttachConsole(AttachParentProcess);
    }

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);

    [DllImport("kernel32.dll")]
    private static extern nint GetStdHandle(int handle);

    [DllImport("kernel32.dll")]
    private static extern int GetFileType(nint handle);
}
