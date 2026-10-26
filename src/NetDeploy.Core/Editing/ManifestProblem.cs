using System.Text.Json.Nodes;

namespace NetDeploy.Core.Editing;

/// <summary>Something that stops a config from being read, and the step it is about, if any.</summary>
public sealed record ManifestProblem(string Message, JsonObject? Step);
