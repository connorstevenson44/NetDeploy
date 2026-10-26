using System.Text.Json;
using System.Text.Json.Serialization;

namespace NetDeploy.Core.Steps;

internal sealed class StepCleanupConverter : JsonConverter<StepCleanup>
{
    public override StepCleanup Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.TokenType switch
    {
        JsonTokenType.String => new(reader.GetString(), null),
        JsonTokenType.StartArray => new(null, JsonSerializer.Deserialize<List<DeployStep>>(ref reader, options)),
        _ => throw new JsonException("cleanup is PowerShell, as a string, or a list of steps."),
    };

    public override void Write(Utf8JsonWriter writer, StepCleanup value, JsonSerializerOptions options)
    {
        if (value.Steps is { } steps)
            JsonSerializer.Serialize(writer, steps, options);
        else
            writer.WriteStringValue(value.PowerShell);
    }
}
