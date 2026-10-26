using System.Text.Json;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using NetDeploy.Core.Editing;

namespace NetDeploy.App.ViewModels;

/// <summary>
/// When a step runs, or which branch an if takes: a question, a comparison, a PowerShell check, or raw JSON for
/// combinations with all, any and not.
/// </summary>
public sealed partial class ConditionFieldViewModel : EditorFieldViewModel
{
    private readonly bool _loading = true;

    public ConditionFieldViewModel(JsonObject owner, SchemaField field, Action changed) : base(owner, field, changed)
    {
        var condition = owner[field.Name] as JsonObject;
        var keys = condition?.Select(pair => pair.Key).ToHashSet() ?? [];
        Ask = Read(condition, "ask");
        Value = Read(condition, "value");
        EqualsValue = Read(condition, "equals");
        Script = Read(condition, "powershell");
        AdvancedJson = condition?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "{\n  \"all\": []\n}";
        Mode = keys switch
        {
            { Count: 0 } => ConditionMode.Always,
            _ when keys.SetEquals(["ask"]) => ConditionMode.Ask,
            _ when keys.IsSubsetOf(["value", "equals"]) => ConditionMode.ValueEquals,
            _ when keys.SetEquals(["powershell"]) => ConditionMode.PowerShell,
            _ => ConditionMode.Advanced,
        };
        _loading = false;
    }

    private static readonly ConditionOption[] AllModes =
    [
        new(ConditionMode.Always, "Always"),
        new(ConditionMode.Ask, "Ask a yes/no question"),
        new(ConditionMode.ValueEquals, "A value equals"),
        new(ConditionMode.PowerShell, "A PowerShell check"),
        new(ConditionMode.Advanced, "Advanced (all, any, not) as JSON"),
    ];

    /// <summary>An if always needs a condition, so "Always" is only offered for a step's "Run only when".</summary>
    public IReadOnlyList<ConditionOption> Modes => Field.Required ? AllModes[1..] : AllModes;

    [ObservableProperty]
    public partial ConditionMode Mode { get; set; }

    [ObservableProperty]
    public partial string Ask { get; set; }

    [ObservableProperty]
    public partial string Value { get; set; }

    [ObservableProperty]
    public partial string EqualsValue { get; set; }

    [ObservableProperty]
    public partial string Script { get; set; }

    [ObservableProperty]
    public partial string AdvancedJson { get; set; }

    [ObservableProperty]
    public partial string? Error { get; set; }

    partial void OnModeChanged(ConditionMode value) => Save();
    partial void OnAskChanged(string value) => Save();
    partial void OnValueChanged(string value) => Save();
    partial void OnEqualsValueChanged(string value) => Save();
    partial void OnScriptChanged(string value) => Save();
    partial void OnAdvancedJsonChanged(string value) => Save();

    private void Save()
    {
        if (_loading)
            return;
        Error = null;
        switch (Mode)
        {
            case ConditionMode.Always:
                Write(null);
                break;
            case ConditionMode.Ask:
                Write(new JsonObject { ["ask"] = Ask });
                break;
            case ConditionMode.ValueEquals:
                Write(new JsonObject { ["value"] = Value, ["equals"] = EqualsValue });
                break;
            case ConditionMode.PowerShell:
                Write(new JsonObject { ["powershell"] = Script });
                break;
            default:
                try
                {
                    Write(JsonNode.Parse(AdvancedJson));
                }
                catch (JsonException ex)
                {
                    Error = $"Not saved: this isn't valid JSON. {ex.Message}";
                }
                break;
        }
    }

    private static string Read(JsonObject? condition, string key) => condition?[key]?.ToString() ?? "";
}
