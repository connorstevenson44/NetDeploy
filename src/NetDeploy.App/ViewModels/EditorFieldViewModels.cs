using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetDeploy.Core.Editing;

namespace NetDeploy.App.ViewModels;

/// <summary>
/// One setting on the editor's form, writing straight into the JSON object that owns it. changed is called after
/// each write, so the editor can mark the file changed and check it again.
/// </summary>
public abstract partial class EditorFieldViewModel(JsonObject owner, SchemaField field, Action changed) : ObservableObject
{
    protected JsonObject Owner { get; } = owner;
    public SchemaField Field { get; } = field;
    public string Label => Field.Label + (Field.Required ? " *" : "");
    public string? Description => Field.Description;
    public FieldKind Kind => Field.Kind;

    /// <summary>False for a group's field that doesn't apply to the kind picked, such as a check's framework for fileExists.</summary>
    [ObservableProperty]
    public partial bool IsShown { get; set; } = true;

    protected void Write(JsonNode? value)
    {
        if (value is null)
            Owner.Remove(Field.Name);
        else
            Owner[Field.Name] = value;
        changed();
    }

    /// <summary>For a value changed in place, inside an object the owner already holds.</summary>
    protected void Changed() => changed();

    /// <summary>The fields of a step or a group, one view model per kind of field.</summary>
    public static IEnumerable<EditorFieldViewModel> For(JsonObject owner, IEnumerable<SchemaField> fields, Action changed) =>
        fields.Select(field => field.Kind switch
        {
            FieldKind.Group => new GroupFieldViewModel(owner, field, changed),
            FieldKind.Condition => new ConditionFieldViewModel(owner, field, changed),
            FieldKind.Inputs => (EditorFieldViewModel)new InputsFieldViewModel(owner, field, changed),
            FieldKind.Cleanup => new CleanupFieldViewModel(owner, field, changed),
            _ => new ValueFieldViewModel(owner, field, changed),
        });
}

/// <summary>Text, a number, yes/no, a choice, a list of lines, or raw JSON for what the form has no editor for.</summary>
public sealed partial class ValueFieldViewModel : EditorFieldViewModel
{
    private readonly bool _loading = true;

    public ValueFieldViewModel(JsonObject owner, SchemaField field, Action changed) : base(owner, field, changed)
    {
        var value = owner[field.Name];
        Text = field.Kind switch
        {
            FieldKind.Lines => string.Join(Environment.NewLine, value?.AsArray().Select(line => line?.ToString()) ?? []),
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

/// <summary>A setting made of settings, such as a prerequisite's check or a task's schedule.</summary>
public sealed class GroupFieldViewModel : EditorFieldViewModel
{
    private readonly JsonObject _group;

    private readonly ValueFieldViewModel? _kind;

    public GroupFieldViewModel(JsonObject owner, SchemaField field, Action changed) : base(owner, field, changed)
    {
        _group = owner[field.Name] as JsonObject ?? new JsonObject();
        Fields = For(_group, field.Fields, OnChildChanged).ToList();
        _kind = Fields.OfType<ValueFieldViewModel>().FirstOrDefault(child => child.Field.Name == "kind");
        ShowFieldsForKind(clear: false);
    }

    public IReadOnlyList<EditorFieldViewModel> Fields { get; }

    // Only the fields for the kind picked are shown. When the kind changes, what was typed for the old one is dropped;
    // a file opened with extra settings keeps them until then.
    private void ShowFieldsForKind(bool clear)
    {
        if (_kind is null)
            return;
        foreach (var child in Fields)
        {
            child.IsShown = child.Field.AppliesTo(_kind.Text);
            if (clear && !child.IsShown && child is ValueFieldViewModel value)
                value.Clear();
        }
    }

    // The group joins the step once something is set in it, and leaves when it is emptied again.
    private void OnChildChanged()
    {
        ShowFieldsForKind(clear: true);
        if (_group.Count == 0 && !Field.Required)
            Write(null);
        else if (_group.Parent is null)
            Write(_group);
        else
            Changed();
    }
}

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

/// <summary>A choice as the schema names it, and its label on the form: fileExists is "File exists".</summary>
public sealed record ChoiceOption(string Value, string Label)
{
    public static IReadOnlyList<ChoiceOption> For(IEnumerable<string> values) =>
        values.Select(value => new ChoiceOption(value, value.Length == 0 ? "" : ManifestSchema.Label(value))).ToList();
}

public enum ConditionMode { Always, Ask, ValueEquals, PowerShell, Advanced }

public sealed record ConditionOption(ConditionMode Mode, string Label);

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

/// <summary>Values asked for when the deploy starts (parameters) or just before a step runs (inputs).</summary>
public sealed partial class InputsFieldViewModel : EditorFieldViewModel
{
    public InputsFieldViewModel(JsonObject owner, SchemaField field, Action changed) : base(owner, field, changed)
    {
        foreach (var (name, definition) in owner[field.Name] as JsonObject ?? [])
            Rows.Add(new InputRowViewModel(this, name, definition as JsonObject ?? []));
    }

    public ObservableCollection<InputRowViewModel> Rows { get; } = [];

    [RelayCommand]
    private void Add()
    {
        var name = Enumerable.Range(Rows.Count + 1, 100).Select(number => $"Value{number}").First(name => Rows.All(row => row.Name != name));
        Rows.Add(new InputRowViewModel(this, name, new JsonObject { ["label"] = "" }));
        Save();
    }

    [RelayCommand]
    private void Remove(InputRowViewModel row)
    {
        Rows.Remove(row);
        Save();
    }

    /// <summary>
    /// Writes the rows back in order. A row whose name is empty or used twice is shown as a problem and the rows aren't
    /// written until it is fixed, so no value is lost by overwriting another.
    /// </summary>
    internal void Save()
    {
        foreach (var row in Rows)
            row.Problem = row.Name.Trim().Length == 0 ? "Give it a name."
                : Rows.Count(other => other.Name.Trim() == row.Name.Trim()) > 1 ? "Two values have this name."
                : null;
        if (Rows.Any(row => row.Problem is not null))
            return;

        var inputs = new JsonObject();
        foreach (var row in Rows)
            inputs[row.Name.Trim()] = row.ToJson();
        Write(inputs.Count == 0 ? null : inputs);
    }
}

/// <summary>One parameter or input: its name, label, type, default and choices.</summary>
public sealed partial class InputRowViewModel : ObservableObject
{
    private readonly InputsFieldViewModel _owner;
    private readonly bool _loading;

    public InputRowViewModel(InputsFieldViewModel owner, string name, JsonObject definition)
    {
        _loading = true;
        _owner = owner;
        Name = name;
        Label = definition["label"]?.ToString() ?? "";
        Type = definition["type"]?.ToString() ?? "text";
        Default = definition["default"]?.ToString() ?? "";
        Options = string.Join(", ", definition["options"] is JsonArray options ? options.Select(option => option?.ToString()) : []);
        Optional = definition["optional"] is JsonValue optional && optional.TryGetValue(out bool isOptional) && isOptional;
        _loading = false;
    }

    public static IReadOnlyList<ChoiceOption> Types { get; } = ChoiceOption.For(["text", "secret", "number", "yesNo", "choice", "folder", "file"]);

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial string Label { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChoice))]
    public partial string Type { get; set; }

    [ObservableProperty]
    public partial string Default { get; set; }

    /// <summary>The choices for a choice input, separated by commas.</summary>
    [ObservableProperty]
    public partial string Options { get; set; }

    [ObservableProperty]
    public partial bool Optional { get; set; }

    [ObservableProperty]
    public partial string? Problem { get; set; }

    public bool IsChoice => Type == "choice";

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (!_loading && e.PropertyName is not (nameof(Problem) or nameof(IsChoice)))
            _owner.Save();
    }

    /// <summary>Only what is set is written, and text is the default type.</summary>
    internal JsonObject ToJson()
    {
        var json = new JsonObject();
        if (Label.Length > 0)
            json["label"] = Label;
        if (Type != "text")
            json["type"] = Type;
        if (Default.Length > 0)
            json["default"] = Default;
        if (IsChoice)
            json["options"] = new JsonArray(Options.Split(',').Select(option => option.Trim()).Where(option => option.Length > 0).Select(option => (JsonNode)JsonValue.Create(option)).ToArray());
        if (Optional)
            json["optional"] = true;
        return json;
    }
}
