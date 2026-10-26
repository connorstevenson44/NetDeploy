namespace NetDeploy.Core.Cli;

/// <summary>A mistake in the command line, explained to the person who typed it.</summary>
public sealed class CliException(string message) : Exception(message);
