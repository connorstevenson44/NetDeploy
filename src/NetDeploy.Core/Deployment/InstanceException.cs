namespace NetDeploy.Core.Deployment;

/// <summary>The chosen install can't be deployed, for example because it has no name or a new install's folder already has files.</summary>
internal sealed class InstanceException(string message) : Exception(message);
