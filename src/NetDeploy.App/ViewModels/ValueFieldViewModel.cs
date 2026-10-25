using System.Text.Json;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using NetDeploy.Core.Editing;

namespace NetDeploy.App.ViewModels;

/// <summary>Text, a number, yes/no, a choice, a list of lines, or raw JSON for what the form has no editor for.</summary>
public sealed partial class ValueFieldViewModel : EditorFieldViewModel
{
    private readonly bool _loading = true;

    public ValueFieldViewModel(JsonObject owner, SchemaField field, Action changed) : base(owner, field, changed)
    {
        var value = owner[field.Name];
        Text = field.Kind switch
        {
            // A file written by hand may have one line where a list goes; it shows as that line until it is saved.
            FieldKind.Lines => value is JsonArray lines ? string.Join(Environment.NewLine, lines.Select(line => line?.ToString())) : value?.ToString() ?? "",
            FieldKind.Json => value?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "",
            _ => value?.ToString() ?? "",
        };
        IsChecked = value is JsonValue flag && flag.TryGetValue(out bool isChecked) && isChecked;
        _loading = false;
    }

    /// <summary>A choice that isn't required can be left unset, shown as an empty option.</summary>
    public IReadOnlyList<ChoiceOption> Options => ChoiceOption.For(Field.Required ? Field.Options : ["", .. Field.Options]);

    /// <summary>Empties the field, for one that no longer applies.</summary>
    public void Clear()
    {
        Text = "";
        IsChecked = false;
    }

    [ObservableProperty]
    public partial string Text { get; set; }

    [ObservableProperty]
    public partial bool IsChecked { get; set; }

    /// <summary>Why raw JSON wasn't saved, or null.</summary>
    [ObservableProperty]
    public partial string? Error { get; set; }

    partial void OnTextChanged(string value)
    {
        if (_loading)
            return;
        Error = null;
        if (value.Trim().Length == 0)
        {
            Write(null);
            return;
        }

        switch (Kind)
        {
            case FieldKind.Number:
                // Anything that isn't a whole number is saved as typed, so the problems list says what is wrong.
                Write(int.TryParse(value, out var number) ? JsonValue.Create(number) : JsonValue.Create(value));
                break;
            case FieldKind.Lines:
                var lines = value.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0);
                Write(new JsonArray(lines.Select(line => (JsonNode)JsonValue.Create(line)).ToArray()));
                break;
            case FieldKind.Json:
                try
                {
                    Write(JsonNode.Parse(value));
                }
                catch (JsonException ex)
                {
                    Error = $"Not saved: this isn't valid JSON. {ex.Message}";
                }
                break;
            default:
                Write(JsonValue.Create(value));
                break;
        }
    }

    // Every yes/no setting is off unless it is set, so off removes it.
    partial void OnIsCheckedChanged(bool value)
    {
        if (!_loading && Kind == FieldKind.YesNo)
            Write(value ? JsonValue.Create(true) : null);
    }
}
