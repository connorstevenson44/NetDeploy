using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.Input;
using NetDeploy.Core.Editing;

namespace NetDeploy.App.ViewModels;

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
