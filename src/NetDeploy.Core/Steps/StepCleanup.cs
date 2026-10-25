using System.Text.Json.Serialization;

namespace NetDeploy.Core.Steps;

/// <summary>
/// A step's "cleanup", run when the install is removed. Written as a string, it is PowerShell run after the step's
/// built-in undo. Written as a list of steps (the older form), it replaces the built-in undo; [] means nothing to undo.
/// </summary>
[JsonConverter(typeof(StepCleanupConverter))]
public sealed record StepCleanup(string? PowerShell, IReadOnlyList<DeployStep>? Steps);
