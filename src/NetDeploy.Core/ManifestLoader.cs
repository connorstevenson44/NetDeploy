using System.Text.Json;
using System.Text.Json.Serialization;

namespace NetDeploy.Core;

public static class ManifestLoader
{
    public const int SupportedSchemaVersion = 1;

    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        // Lets "type" appear anywhere in a step, not only first.
        AllowOutOfOrderMetadataProperties = true,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        // Catches typos such as "timoutMinutes" instead of silently ignoring them.
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static DeployManifest Parse(string json)
    {
        DeployManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<DeployManifest>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new ManifestException([ex.Message]);
        }

        if (manifest is null)
            throw new ManifestException(["deploy.json is empty."]);

        var errors = Validate(manifest).ToList();
        if (errors.Count > 0)
            throw new ManifestException(errors);

        return manifest;
    }

    private static IEnumerable<string> Validate(DeployManifest manifest)
    {
        if (manifest.SchemaVersion != SupportedSchemaVersion)
            yield return $"schemaVersion {manifest.SchemaVersion} is not supported. This version of NetDeploy reads schemaVersion {SupportedSchemaVersion}.";

        if (manifest.Steps.Count == 0)
            yield return "The manifest has no steps.";

        foreach (var (name, field) in manifest.Parameters)
            foreach (var error in field.Validate(name))
                yield return error;

        foreach (var error in ValidateSteps(manifest.Steps, "steps"))
            yield return error;
    }

    internal static IEnumerable<string> ValidateSteps(IReadOnlyList<DeployStep> steps, string path)
    {
        for (var i = 0; i < steps.Count; i++)
            foreach (var error in steps[i].Validate())
                yield return $"{path}[{i}]: {error}";
    }
}

public sealed class ManifestException(IReadOnlyList<string> errors)
    : Exception("deploy.json has problems:" + Environment.NewLine + string.Join(Environment.NewLine, errors.Select(e => "- " + e)))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}
