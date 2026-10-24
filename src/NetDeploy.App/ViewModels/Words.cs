namespace NetDeploy.App.ViewModels;

/// <summary>Small helpers for text the UI shows.</summary>
public static class Words
{
    /// <summary>A count and its noun: "1 file", "3 files".</summary>
    public static string Count(int count, string one, string many) => $"{count} {(count == 1 ? one : many)}";
}
