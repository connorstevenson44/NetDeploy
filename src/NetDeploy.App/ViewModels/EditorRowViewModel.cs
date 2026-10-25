using System.Text.Json.Nodes;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using MaterialDesignThemes.Wpf;
using NetDeploy.Core.Editing;

namespace NetDeploy.App.ViewModels;

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
