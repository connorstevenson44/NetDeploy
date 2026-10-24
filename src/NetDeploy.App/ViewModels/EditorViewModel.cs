using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MaterialDesignThemes.Wpf;
using NetDeploy.Core;
using NetDeploy.Core.Editing;

namespace NetDeploy.App.ViewModels;

public enum EditorRowKind { Settings, Step, Branch }

/// <summary>
/// A row in the editor's outline: the app settings, a step, or the Then or Else branch of an if, which holds the
/// steps indented under it.
/// </summary>
public sealed partial class EditorRowViewModel : ObservableObject
{
    private EditorRowViewModel(EditorRowKind kind, int depth, JsonObject? step, string? branch)
    {
        Kind = kind;
        Step = step;
        Branch = branch;
        Indent = new Thickness(depth * 20, 0, 0, 0);
        Title = "";
    }

    public EditorRowKind Kind { get; }

    /// <summary>The step this row shows; for a branch row, the if it belongs to.</summary>
    public JsonObject? Step { get; }

    /// <summary>"then" or "else" for a branch row.</summary>
    public string? Branch { get; }

    public Thickness Indent { get; }

    [ObservableProperty]
    public partial string Title { get; set; }

    [ObservableProperty]
    public partial bool HasProblem { get; set; }

    public PackIconKind Icon => Kind switch
    {
        EditorRowKind.Settings => PackIconKind.TuneVariant,
        EditorRowKind.Branch => Branch == "then" ? PackIconKind.CheckCircleOutline : PackIconKind.CloseCircleOutline,
        _ => IconFor(ManifestDocument.TypeOf(Step!)),
    };

    public static PackIconKind IconFor(string? type) => type switch
    {
        "checkPrerequisite" => PackIconKind.ClipboardCheckOutline,
        "service" => PackIconKind.CogOutline,
        "backup" => PackIconKind.ArchiveOutline,
        "copyFiles" => PackIconKind.ContentCopy,
        "runExe" => PackIconKind.Play,
        "powershell" => PackIconKind.Console,
        "scheduledTask" => PackIconKind.CalendarClock,
        "if" => PackIconKind.SourceBranch,
        "deleteFolder" => PackIconKind.FolderRemoveOutline,
        _ => PackIconKind.HelpCircleOutline,
    };

    public static EditorRowViewModel Settings() => new(EditorRowKind.Settings, 0, null, null) { Title = "App and parameters" };

    public static IEnumerable<EditorRowViewModel> Flatten(JsonArray steps, int depth = 0)
    {
        foreach (var step in steps.OfType<JsonObject>())
        {
            yield return new EditorRowViewModel(EditorRowKind.Step, depth, step, null) { Title = ManifestDocument.TitleOf(step) };
            if (ManifestDocument.TypeOf(step) != "if")
                continue;
            foreach (var branch in new[] { "then", "else" })
            {
                yield return new EditorRowViewModel(EditorRowKind.Branch, depth + 1, step, branch) { Title = branch == "then" ? "Then" : "Else" };
                foreach (var nested in Flatten(step[branch] as JsonArray ?? [], depth + 2))
                    yield return nested;
            }
        }
    }
}

/// <summary>A problem in the problems list; clicking it selects the step it is about.</summary>
public sealed record EditorProblemViewModel(string Message, string Where, JsonObject? Step);

/// <summary>
/// Creates or edits a deploy.json: an outline of the steps on the left, a form for the selected one on the right,
/// and the problems NetDeploy would report, checked after every change.
/// </summary>
public sealed partial class EditorViewModel : ObservableObject
{
    private static readonly SchemaField ParametersField =
        new("parameters", FieldKind.Inputs, "Asked once, before the first step. Steps use them as {Name}.", false, [], []);

    private ManifestDocument _document = ManifestDocument.New();
    private string? _name;

    public EditorViewModel() => Reset(null);

    public ObservableCollection<EditorRowViewModel> Rows { get; } = [];
    public ObservableCollection<EditorFieldViewModel> Fields { get; } = [];
    public ObservableCollection<EditorProblemViewModel> Problems { get; } = [];
    public IReadOnlyList<StepSchema> StepTypes => ManifestSchema.Steps;

    /// <summary>Where the config is saved; null for a new one not saved yet.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    public partial string? FilePath { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    public partial bool IsDirty { get; private set; }

    public string Title => (FilePath is not null ? Path.GetFileName(Path.GetDirectoryName(FilePath)) + @"\" + Path.GetFileName(FilePath)
        : _name ?? "New deploy config") + (IsDirty ? " •" : "");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSettingsSelected), nameof(SelectedSchema), nameof(SelectedTitle), nameof(SelectedIcon))]
    [NotifyCanExecuteChangedFor(nameof(MoveUpCommand), nameof(MoveDownCommand), nameof(DeleteCommand))]
    public partial EditorRowViewModel? Selected { get; set; }

    public bool IsSettingsSelected => Selected?.Kind is null or EditorRowKind.Settings;

    /// <summary>The selected step's type, for its description; null for the settings or a branch.</summary>
    public StepSchema? SelectedSchema => Selected is { Kind: EditorRowKind.Step, Step: { } step } ? ManifestSchema.For(ManifestDocument.TypeOf(step)) : null;

    public string SelectedTitle => Selected?.Kind switch
    {
        EditorRowKind.Step => SelectedSchema?.Label ?? "Unknown step",
        EditorRowKind.Branch => Selected.Branch == "then" ? "Then: steps run when the condition is true" : "Else: steps run when it is false",
        _ => "App and parameters",
    };

    public PackIconKind SelectedIcon => Selected?.Icon ?? PackIconKind.TuneVariant;

    public bool HasProblems => Problems.Count > 0;

    public string ProblemSummary => Problems.Count switch
    {
        0 => "No problems: NetDeploy can read this config.",
        1 => "1 problem",
        var count => $"{count} problems",
    };

    // ---- App settings, edited on the settings form

    public string AppName
    {
        get => App["name"]?.ToString() ?? "";
        set => SetText(App, "name", value);
    }

    public string AppVersion
    {
        get => App["version"]?.ToString() ?? "";
        set => SetText(App, "version", value);
    }

    public bool RequiresAdmin
    {
        get => _document.Root["requiresAdmin"] is JsonValue flag && flag.TryGetValue(out bool value) && value;
        set => SetFlag(_document.Root, "requiresAdmin", value);
    }

    /// <summary>Empty means the app has no install block, so steps don't get {InstanceDir}.</summary>
    public string InstallFolder
    {
        get => _document.Root["install"]?["folder"]?.ToString() ?? "";
        set
        {
            if (value.Length == 0)
                _document.Root.Remove("install");
            else
                Install["folder"] = value;
            OnChanged();
            OnPropertyChanged(nameof(HasInstall));
        }
    }

    public bool HasInstall => _document.Root["install"] is JsonObject;

    public bool Instances
    {
        get => _document.Root["install"]?["instances"] is JsonValue flag && flag.TryGetValue(out bool value) && value;
        set => SetFlag(Install, "instances", value);
    }

    [ObservableProperty]
    public partial InputsFieldViewModel? Parameters { get; private set; }

    private JsonObject App => (_document.Root["app"] ??= new JsonObject()).AsObject();
    private JsonObject Install => (_document.Root["install"] ??= new JsonObject()).AsObject();

    // ---- Loading and saving

    public void New() => Reset(null, ManifestDocument.New());

    /// <summary>Shows a config that has no file of its own, such as the deploy.json in a zip; Save asks where to save it.</summary>
    public void Load(string json, string name)
    {
        Reset(null, ManifestDocument.Parse(json));
        _name = name;
        OnPropertyChanged(nameof(Title));
    }

    /// <summary>Opens a deploy.json. Throws ManifestException when it isn't JSON, and IOException when it can't be read.</summary>
    public void Open(string path) => Reset(path, ManifestDocument.Parse(File.ReadAllText(path)));

    /// <summary>A warning when the file was saved by another version of NetDeploy, or null.</summary>
    public string? VersionWarning => ManifestLoader.VersionWarning(_document.SavedWith);

    public void Save(string path)
    {
        _document.StampVersion();
        File.WriteAllText(path, _document.ToJson());
        FilePath = path;
        IsDirty = false;
    }

    /// <summary>The config as it would be saved, for tests and the JSON view.</summary>
    public string Json => _document.ToJson();

    private void Reset(string? path, ManifestDocument? document = null)
    {
        _document = document ?? ManifestDocument.New();
        _name = null;
        FilePath = path;
        Parameters = new InputsFieldViewModel(_document.Root, ParametersField, OnChanged);
        RebuildRows(select: null);
        OnPropertyChanged(string.Empty);
        Check();
        IsDirty = false;
    }

    // ---- Changing the steps

    /// <summary>Adds a step after the selected one, at the end of the selected branch, or at the end of the config.</summary>
    [RelayCommand]
    private void AddStep(StepSchema type)
    {
        var step = type.NewStep();
        switch (Selected)
        {
            case { Kind: EditorRowKind.Step, Step: { } after }:
                var list = after.Parent!.AsArray();
                list.Insert(list.IndexOf(after) + 1, step);
                break;
            case { Kind: EditorRowKind.Branch, Step: { } ifStep, Branch: { } branch }:
                (ifStep[branch] ??= new JsonArray()).AsArray().Add(step);
                break;
            default:
                _document.Steps.Add(step);
                break;
        }
        OnStructureChanged(step);
    }

    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private void MoveUp() => Move(-1);

    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private void MoveDown() => Move(1);

    private bool CanMoveUp => SelectedStepList is { } list && list.IndexOf(Selected!.Step) > 0;
    private bool CanMoveDown => SelectedStepList is { } list && list.IndexOf(Selected!.Step) < list.Count - 1;

    private JsonArray? SelectedStepList => Selected is { Kind: EditorRowKind.Step, Step.Parent: JsonArray list } ? list : null;

    private void Move(int offset)
    {
        var step = Selected!.Step!;
        var list = SelectedStepList!;
        var index = list.IndexOf(step);
        list.RemoveAt(index);
        list.Insert(index + offset, step);
        OnStructureChanged(step);
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private void Delete()
    {
        var list = SelectedStepList!;
        var index = list.IndexOf(Selected!.Step);
        list.RemoveAt(index);
        // Select the step that took its place, or the one before it, so deleting several in a row is easy.
        OnStructureChanged(index < list.Count ? list[index] as JsonObject : index > 0 ? list[index - 1] as JsonObject : null);
    }

    private bool CanDelete => SelectedStepList is not null;

    [RelayCommand]
    private void SelectProblem(EditorProblemViewModel problem) => Selected = RowFor(problem.Step);

    /// <summary>
    /// The step's row; for a cleanup step, which the outline doesn't list, the row of the step it undoes; and for a
    /// problem with the config as a whole, the settings row.
    /// </summary>
    private EditorRowViewModel RowFor(JsonNode? step)
    {
        for (; step is not null; step = step.Parent)
            if (Rows.FirstOrDefault(row => row.Kind == EditorRowKind.Step && row.Step == step) is { } row)
                return row;
        return Rows[0];
    }

    private void OnStructureChanged(JsonObject? select)
    {
        RebuildRows(select);
        OnChanged();
    }

    private void RebuildRows(JsonObject? select)
    {
        Rows.Clear();
        Rows.Add(EditorRowViewModel.Settings());
        foreach (var row in EditorRowViewModel.Flatten(_document.Steps))
            Rows.Add(row);
        Selected = Rows.FirstOrDefault(row => row.Kind == EditorRowKind.Step && row.Step == select) ?? Rows[0];
    }

    partial void OnSelectedChanged(EditorRowViewModel? value)
    {
        Fields.Clear();
        if (value is { Kind: EditorRowKind.Step, Step: { } step } && ManifestSchema.For(ManifestDocument.TypeOf(step)) is { } schema)
            foreach (var field in EditorFieldViewModel.For(step, schema.Fields, OnChanged))
                Fields.Add(field);
    }

    // ---- After every change

    private void OnChanged()
    {
        IsDirty = true;
        foreach (var row in Rows.Where(row => row.Kind == EditorRowKind.Step))
            row.Title = ManifestDocument.TitleOf(row.Step!);
        foreach (var cleanup in Fields.OfType<CleanupFieldViewModel>())
            cleanup.Refresh();
        Check();
    }

    private void Check()
    {
        Problems.Clear();
        foreach (var problem in _document.Problems())
            Problems.Add(new EditorProblemViewModel(problem.Message, problem.Step is { } step ? ManifestDocument.TitleOf(step) : "Config", problem.Step));
        var withProblems = Problems.Select(problem => RowFor(problem.Step)).ToHashSet();
        foreach (var row in Rows)
            row.HasProblem = withProblems.Contains(row);
        OnPropertyChanged(nameof(HasProblems));
        OnPropertyChanged(nameof(Json));
        OnPropertyChanged(nameof(ProblemSummary));
    }

    private void SetText(JsonObject owner, string name, string value, [System.Runtime.CompilerServices.CallerMemberName] string property = "")
    {
        owner[name] = value;
        OnChanged();
        OnPropertyChanged(property);
    }

    private void SetFlag(JsonObject owner, string name, bool value, [System.Runtime.CompilerServices.CallerMemberName] string property = "")
    {
        if (value)
            owner[name] = true;
        else
            owner.Remove(name);
        OnChanged();
        OnPropertyChanged(property);
    }
}
