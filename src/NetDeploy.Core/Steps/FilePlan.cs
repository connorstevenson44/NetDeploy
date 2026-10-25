namespace NetDeploy.Core.Steps;

/// <summary>Every file a copy step will look at: the package's files and the ones only on the server.</summary>
public sealed record FilePlan(string Source, string Target, IReadOnlyList<FilePlanEntry> Entries);
