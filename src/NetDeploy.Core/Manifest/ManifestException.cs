namespace NetDeploy.Core.Manifest;

public sealed class ManifestException(IReadOnlyList<string> errors, Exception? inner = null)
    : Exception("deploy.json has problems:" + Environment.NewLine + string.Join(Environment.NewLine, errors.Select(e => "- " + e)), inner)
{
    public IReadOnlyList<string> Errors { get; } = errors;
}
