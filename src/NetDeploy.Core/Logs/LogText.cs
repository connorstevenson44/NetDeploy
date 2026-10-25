namespace NetDeploy.Core.Logs;

/// <summary>
/// A log's text to show: all of it, or for a long log, its last lines. Tail carries on from where it ends, for a live
/// log; it is null for a zipped log, which doesn't grow.
/// </summary>
public sealed record LogText(string Text, bool IsCut, long Size, LogTail? Tail);
