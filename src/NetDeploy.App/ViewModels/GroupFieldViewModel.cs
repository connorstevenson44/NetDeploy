using System.Text.Json.Nodes;
using NetDeploy.Core.Editing;

namespace NetDeploy.App.ViewModels;

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
