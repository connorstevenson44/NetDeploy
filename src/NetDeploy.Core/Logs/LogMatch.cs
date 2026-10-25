namespace NetDeploy.Core.Logs;

/// <summary>Where a match is in a log's text.</summary>
public readonly record struct LogMatch(int Start, int Length);
