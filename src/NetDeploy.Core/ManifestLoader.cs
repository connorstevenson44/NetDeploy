using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

using NetDeploy.Core.Steps;

namespace NetDeploy.Core;

public static partial class ManifestLoader
{
    public const int SupportedSchemaVersion = 1;

    /// <summary>This NetDeploy's version, as the editor saves it in netdeployVersion.</summary>
    public static string NetDeployVersion { get; } =
        typeof(ManifestLoader).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];

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
            throw new ManifestException([ex.Message], ex);
        }

        if (manifest is null)
            throw new ManifestException(["deploy.json is empty."]);

        var errors = Validate(manifest).ToList();
        if (errors.Count > 0)
            throw new ManifestException(errors);

        return manifest;
    }

    /// <summary>
    /// Reads a deploy.json as well as it can, for opening a package. A setting this NetDeploy doesn't know, such as one
    /// from another version, is left out with a warning instead of stopping the package from opening; anything else
    /// wrong still throws. A file saved by another version of NetDeploy gets a warning too.
    /// </summary>
    public static (DeployManifest Manifest, IReadOnlyList<string> Warnings) ParseLeniently(string json)
    {
        JsonNode root;
        try
        {
            root = JsonNode.Parse(json, documentOptions: new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })
                ?? throw new ManifestException(["deploy.json is empty."]);
        }
        catch (JsonException ex)
        {
            throw new ManifestException([ex.Message]);
        }

        var warnings = new List<string>();
        while (true)
        {
            try
            {
                var manifest = Parse(root.ToJsonString());
                if (VersionWarning(manifest.NetDeployVersion) is { } versionWarning)
                    warnings.Insert(0, versionWarning);
                return (manifest, warnings);
            }
            catch (ManifestException ex) when (ex.InnerException is JsonException { Path: { } path } unknown
                && unknown.Message.Contains("could not be mapped") && Remove(root, path))
            {
                warnings.Add($"Left out {path[2..]}: this version of NetDeploy doesn't know that setting.");
            }
        }
    }

    /// <summary>A warning when the file was saved by another version of NetDeploy, or null.</summary>
    public static string? VersionWarning(string? savedWith)
    {
        if (savedWith is null || savedWith == NetDeployVersion)
            return null;
        var newer = Version.TryParse(savedWith, out var saved) && Version.TryParse(NetDeployVersion, out var current) && saved > current;
        return newer
            ? $"This deploy.json was saved by NetDeploy {savedWith}, which is newer than this one ({NetDeployVersion}). Check the steps before you deploy, or use NetDeploy {savedWith}."
            : $"This deploy.json was saved by NetDeploy {savedWith}; this is {NetDeployVersion}. Check the steps before you deploy, and save it from the editor to update it.";
    }

    // Removes the setting at a path such as "$.steps[2].timoutMinutes", the reader's name for where it stopped.
    private static bool Remove(JsonNode root, string path)
    {
        var segments = PathSegment().Matches(path[1..]).ToList();
        JsonNode? node = root;
        foreach (var segment in segments[..^1])
            node = segment.Groups["index"].Success ? node?[int.Parse(segment.Groups["index"].Value, CultureInfo.InvariantCulture)] : node?[Name(segment)];
        return segments.Count > 0 && node is JsonObject owner && owner.Remove(Name(segments[^1]));

        static string Name(Match segment) => segment.Groups["name"].Success ? segment.Groups["name"].Value : segment.Groups["quoted"].Value;
    }

    [GeneratedRegex(@"\.(?<name>[^.\[]+)|\[(?<index>\d+)\]|\['(?<quoted>[^']+)'\]")]
    private static partial Regex PathSegment();

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

public sealed class ManifestException(IReadOnlyList<string> errors, Exception? inner = null)
    : Exception("deploy.json has problems:" + Environment.NewLine + string.Join(Environment.NewLine, errors.Select(e => "- " + e)), inner)
{
    public IReadOnlyList<string> Errors { get; } = errors;
}
