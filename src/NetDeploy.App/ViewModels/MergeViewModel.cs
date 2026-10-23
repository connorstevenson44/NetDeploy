using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetDeploy.Core.Config;

namespace NetDeploy.App.ViewModels;

public sealed record ChoiceOption(MergeChoice Value, string Label);

/// <summary>One row of the merge table. Changes go straight to the underlying MergeRow.</summary>
public sealed class MergeRowViewModel(MergeRow row, IConfigFormat format, Action changed) : ObservableObject
{
    public static IReadOnlyList<ChoiceOption> Choices { get; } =
    [
        new(MergeChoice.KeepCurrent, "Keep current"),
        new(MergeChoice.TakeIncoming, "Take incoming"),
        new(MergeChoice.Edit, "Edit value"),
        new(MergeChoice.Remove, "Remove"),
    ];

    public string Path => row.Path;
    public string Section => row.Path.Contains(':') ? row.Path[..row.Path.IndexOf(':')] : "(top level)";
    public string CurrentText => Show(row.Current);
    public string IncomingText => Show(row.Incoming);
    public string Reason => row.Reason;
    public bool IsChanged => row.IsChanged;
    public bool NeedsChoice => row.Choice is null;

    public MergeChoice? Choice
    {
        get => row.Choice;
        set
        {
            // A ComboBox in a recycled grid row can push null; every real choice has a value.
            if (value is null)
                return;

            // Start an edit from what the row shows now, so picking "Edit value" alone changes nothing.
            if (value == MergeChoice.Edit && row.EditedValue is null)
                row.EditedValue = row.Choice is null ? row.Current ?? row.Incoming : row.Result;
            row.Choice = value;
            OnChanged();
        }
    }

    /// <summary>Typing a result switches the row to "Edit value".</summary>
    public string ResultText
    {
        get => row.Choice is null ? "" : Show(row.Result);
        set
        {
            row.EditedValue = format.ParseEdit(row.Current ?? row.Incoming!, value);
            row.Choice = MergeChoice.Edit;
            OnChanged();
        }
    }

    private string Show(string? value) => value is null ? "(not set)" : format.Display(value);

    private void OnChanged()
    {
        OnPropertyChanged(nameof(Choice));
        OnPropertyChanged(nameof(ResultText));
        OnPropertyChanged(nameof(NeedsChoice));
        changed();
    }
}

public sealed partial class MergeViewModel : ObservableObject
{
    private readonly MergePlan _plan;

    public MergeViewModel(string filePath, MergePlan plan, bool isReadOnly)
    {
        _plan = plan;
        FilePath = filePath;
        IsReadOnly = isReadOnly;
        Rows = plan.Rows.Select(row => new MergeRowViewModel(row, plan.Format, OnRowChanged)).ToList();
        RowsView = CollectionViewSource.GetDefaultView(Rows);
        RowsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(MergeRowViewModel.Section)));
        RowsView.Filter = row => ShowUnchanged || ((MergeRowViewModel)row).IsChanged;
    }

    public string FilePath { get; }

    /// <summary>True in a dry run: the table shows the merge but nothing is written.</summary>
    public bool IsReadOnly { get; }

    public bool IsEditable => !IsReadOnly;
    public IReadOnlyList<MergeRowViewModel> Rows { get; }
    public ICollectionView RowsView { get; }
    public string Summary => _plan.Summary;
    public string ApplyText => IsReadOnly ? "Continue dry run" : "Apply merge";
    public bool CanApply => IsReadOnly || _plan.ConflictCount == 0;

    [ObservableProperty]
    public partial bool ShowUnchanged { get; set; }

    partial void OnShowUnchangedChanged(bool value) => RowsView.Refresh();

    [RelayCommand(CanExecute = nameof(IsEditable))]
    private void KeepAllCurrent(string section) => SetSection(section, MergeChoice.KeepCurrent);

    [RelayCommand(CanExecute = nameof(IsEditable))]
    private void TakeAllIncoming(string section) => SetSection(section, MergeChoice.TakeIncoming);

    private void SetSection(string section, MergeChoice choice)
    {
        foreach (var row in Rows.Where(row => row.Section == section && row.IsChanged))
            row.Choice = choice;
    }

    private void OnRowChanged()
    {
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(CanApply));
    }
}
