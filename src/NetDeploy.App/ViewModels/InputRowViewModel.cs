using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NetDeploy.App.ViewModels;

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
