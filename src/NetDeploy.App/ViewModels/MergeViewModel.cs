using CommunityToolkit.Mvvm.ComponentModel;
using NetDeploy.Core.Config;

namespace NetDeploy.App.ViewModels;

/// <summary>The config review: every config file the deploy will write.</summary>
public sealed partial class MergeViewModel : ObservableObject
{
    public MergeViewModel(IReadOnlyList<ConfigFileUpdate> files, bool isReadOnly)
    {
        IsReadOnly = isReadOnly;
        Files = files.Select(file => new ConfigFileViewModel(file, OnChanged)).ToList();
        SelectedFile = Files.FirstOrDefault(file => file.HasConflicts) ?? Files.FirstOrDefault(file => file.NeedsReview) ?? Files[0];
    }

    /// <summary>True in a dry run: the review shows the merge but nothing is written.</summary>
    public bool IsReadOnly { get; }

    public bool IsEditable => !IsReadOnly;
    public IReadOnlyList<ConfigFileViewModel> Files { get; }
    public string ApplyText => IsReadOnly ? "Continue" : "Write files";
    public bool CanApply => IsReadOnly || Files.All(file => !file.HasConflicts && !file.HasTextError);
    public int ConflictCount => Files.Sum(file => file.ConflictCount);
    public int ChangeCount => Files.Sum(file => file.ChangeCount);

    public string Title => $"{Files.Count} config file{(Files.Count == 1 ? "" : "s")}";

    [ObservableProperty]
    public partial ConfigFileViewModel SelectedFile { get; set; }

    private void OnChanged()
    {
        OnPropertyChanged(nameof(ConflictCount));
        OnPropertyChanged(nameof(ChangeCount));
        OnPropertyChanged(nameof(CanApply));
    }
}
