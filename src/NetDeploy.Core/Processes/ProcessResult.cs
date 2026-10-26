namespace NetDeploy.Core.Processes;

internal sealed record ProcessResult(int ExitCode, string? LastLine);
