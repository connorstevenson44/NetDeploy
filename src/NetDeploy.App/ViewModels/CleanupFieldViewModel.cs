using System.Text.Json;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using NetDeploy.Core.Editing;

namespace NetDeploy.App.ViewModels;

/// <summary>
/// What removing the install does for a step: the undo NetDeploy does by itself, shown for reference, then PowerShell to
/// run after it. A file with cleanup written as steps (the older form) keeps them, edited as JSON.
/// </summary>
public sealed partial class CleanupFieldViewModel : EditorFieldViewModel
{
    private readonly bool _loading = true;

    public CleanupFieldViewModel(JsonObject owner, SchemaField field, Action changed) : base(owner, field, changed)
    {
        var value = owner[field.Name];
        IsSteps = value is JsonArray;
        Script = IsSteps ? "" : value?.ToString() ?? "";
        StepsJson = IsSteps ? value!.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) : "";
        _loading = false;
    }

    /// <summary>One line per thing undone by itself, or a line saying there is nothing.</summary>
    public string Automatic => ManifestDocument.AutomaticCleanupOf(Owner) switch
    {
        [] => "Nothing is undone by itself for this step.",
        var undo => "Undone by itself: " + string.Join(" ", undo),
    };

    /// <summary>True for cleanup written as steps, which replace the undo above.</summary>
    [ObservableProperty]
    public partial bool IsSteps { get; private set; }

    [ObservableProperty]
    public partial string Script { get; set; }

    [ObservableProperty]
    public partial string StepsJson { get; set; }

    [ObservableProperty]
    public partial string? Error { get; set; }

    /// <summary>Called after any change to the step, since its settings decide what is undone by itself.</summary>
    internal void Refresh() => OnPropertyChanged(nameof(Automatic));

    partial void OnScriptChanged(string value)
    {
        if (!_loading)
            Write(value.Trim().Length == 0 ? null : JsonValue.Create(value));
    }

    // Emptying the steps goes back to PowerShell.
    partial void OnStepsJsonChanged(string value)
    {
        if (_loading)
            return;
        Error = null;
        if (value.Trim().Length == 0)
        {
            IsSteps = false;
            Write(null);
            return;
        }
        try
        {
            Write(JsonNode.Parse(value) as JsonArray ?? throw new JsonException("Cleanup steps are a list: [ ... ]."));
        }
        catch (JsonException ex)
        {
            Error = $"Not saved: {ex.Message}";
        }
    }
}
