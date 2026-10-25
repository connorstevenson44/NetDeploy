using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
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
