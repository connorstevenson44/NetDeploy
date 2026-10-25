using System.Text.Json.Nodes;

namespace NetDeploy.App.ViewModels;

/// <summary>A problem in the problems list; clicking it selects the step it is about.</summary>
public sealed record EditorProblemViewModel(string Message, string Where, JsonObject? Step);
