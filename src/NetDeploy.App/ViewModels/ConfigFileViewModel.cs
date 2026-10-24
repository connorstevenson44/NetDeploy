using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetDeploy.Core.Config;

namespace NetDeploy.App.ViewModels;

/// <summary>What will happen to a setting, shown as an icon and a colour.</summary>
public enum SettingStatus { Unchanged, Added, Removed, Changed, Kept, Edited, NeedsValue }

/// <summary>A section heading in the settings list, such as "Logging".</summary>
public sealed record SectionViewModel(string Name, int Depth)
{
    public Thickness Indent => new(Depth * 16, 0, 0, 0);
}

/// <summary>
/// One setting, side by side: the value now on the left (read-only) and the value that will be written on the
/// right (editable). Changes go straight to the underlying MergeRow.
/// </summary>
public sealed partial class SettingViewModel : ObservableObject
{
    private readonly MergeRow _row;
    private readonly IConfigFormat _format;
    private readonly Action _changed;

    public SettingViewModel(MergeRow row, IConfigFormat format, Action changed)
    {
        _row = row;
        _format = format;
        _changed = changed;
        var segments = format.SplitPath(row.Path);
        Sections = segments.Take(segments.Count - 1).ToList();
        Key = segments[^1];
    }

    public string Path => _row.Path;
    public IReadOnlyList<string> Sections { get; }
    public string Key { get; }
    public Thickness Indent => new(Sections.Count * 16, 0, 0, 0);

    /// <summary>The value on the server now, or null when the setting is new.</summary>
    public string? Now => Show(_row.Current);

    /// <summary>The value the package brings, or what undo returns to in the editor.</summary>
    public string? Default => Show(_row.Incoming);

    public bool IsRemoved => _row.Choice == MergeChoice.Remove;
    public bool NeedsValue => _row.Choice is null;

    /// <summary>True when the row is worth showing with unchanged settings hidden.</summary>
    public bool IsNotable => Status != SettingStatus.Unchanged;

    /// <summary>The value that will be written, as a person reads and types it.</summary>
    public string Value
    {
        get => Show(Result) ?? "";
        set
        {
            if (IsRemoved || value == Value && _row.Choice is not null)
                return;

            // Typing a value that is already on one side picks that side, so the file keeps its exact text.
            if (_row.Incoming is { } incoming && value == _format.Display(incoming))
                Choose(MergeChoice.TakeIncoming);
            else if (_row.Current is { } current && value == _format.Display(current))
                Choose(MergeChoice.KeepCurrent);
            else
            {
                _row.EditedValue = _format.ParseEdit(_row.Incoming ?? _row.Current ?? "\"\"", value);
                Choose(MergeChoice.Edit);
            }
        }
    }

    public SettingStatus Status => _row.Choice switch
    {
        null => SettingStatus.NeedsValue,
        MergeChoice.Remove => _row.Current is null ? SettingStatus.Unchanged : SettingStatus.Removed,
        MergeChoice.Edit => SettingStatus.Edited,
        _ when !_row.IsChanged => SettingStatus.Unchanged,
        _ when Result == _row.Current => SettingStatus.Kept,
        _ when _row.Current is null => SettingStatus.Added,
        _ => SettingStatus.Changed,
    };

    /// <summary>Hover text for the status icon: why the row is in this state.</summary>
    public string StatusTip => Status switch
    {
        SettingStatus.Kept => $"{_row.Reason}. Keeps the value on the server; the package has {Default ?? "nothing"}.",
        SettingStatus.Edited => "Your value.",
        SettingStatus.Unchanged => "No change.",
        _ => _row.Reason + ".",
    };

    public bool CanKeepNow => _row.Current is not null && (NeedsValue || Result != _row.Current);
    public bool CanReset => _row.Incoming is not null && (NeedsValue || Result != _row.Incoming);
    public bool CanRemove => !IsRemoved;

    [RelayCommand]
    private void KeepNow() => Choose(MergeChoice.KeepCurrent);

    [RelayCommand]
    private void Reset() => Choose(MergeChoice.TakeIncoming);

    [RelayCommand]
    private void Remove() => Choose(MergeChoice.Remove);

    private void Choose(MergeChoice choice)
    {
        _row.Choice = choice;
        OnPropertyChanged(string.Empty);
        _changed();
    }

    /// <summary>The value to write, or null while the row still needs a choice.</summary>
    private string? Result => NeedsValue ? null : _row.Result;

    private string? Show(string? value) => value is null ? null : _format.Display(value);
}

/// <summary>One config file: its settings grouped by section, ready for the side-by-side editor.</summary>
public sealed partial class ConfigFileViewModel : ObservableObject
{
    private readonly ConfigFileUpdate _update;
    private readonly Action _changed;
    private readonly List<SettingViewModel> _settings;
    private bool _showUnchanged;

    public ConfigFileViewModel(ConfigFileUpdate update, Action changed)
    {
        _update = update;
        _changed = changed;
        var format = update.Plan.Format;
        _settings = GroupBySection(update.Plan.Rows.Select(row => new SettingViewModel(row, format, OnSettingChanged)).ToList());
        Items = BuildItems();
    }

    public ConfigFileUpdate Update => _update;
    public string FileName => _update.FileName;
    public string Folder => Path.GetDirectoryName(_update.TargetPath) ?? "";
    public ConfigFileKind Kind => _update.Kind;
    public bool NeedsReview => _update.NeedsReview;
    public int ChangeCount => _settings.Count(setting => setting.Status != SettingStatus.Unchanged);
    public int ConflictCount => _settings.Count(setting => setting.NeedsValue);
    public bool HasConflicts => ConflictCount > 0;
    public bool HasChanges => ChangeCount > 0;

    public string KindTip => _update.Kind switch
    {
        ConfigFileKind.New => "New file: the server has no copy yet.",
        ConfigFileKind.Override => "Server-only override, checked against the new appsettings.json.",
        ConfigFileKind.Edit => "Your edits to this file.",
        _ => "The package's file merged with the server's copy.",
    };

    /// <summary>Column headings: the left side is always what is there now.</summary>
    public string NowHeading => _update.Kind == ConfigFileKind.New ? "Now (new file)" : "Now";
    public string ValueHeading => _update.Kind == ConfigFileKind.Edit ? "New value" : "After deploy";
    public string ResetTip => _update.Kind == ConfigFileKind.Edit ? "Undo" : "Use the package value";

    /// <summary>Section headings and settings, in file order.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<object> Items { get; private set; }

    public void ShowUnchanged(bool show)
    {
        _showUnchanged = show;
        Items = BuildItems();
    }

    [RelayCommand]
    private void KeepAllNow()
    {
        foreach (var setting in _settings.Where(setting => setting.IsNotable && setting.CanKeepNow))
            setting.KeepNowCommand.Execute(null);
    }

    [RelayCommand]
    private void ResetAll()
    {
        foreach (var setting in _settings.Where(setting => setting.CanReset))
            setting.ResetCommand.Execute(null);
    }

    private void OnSettingChanged()
    {
        OnPropertyChanged(nameof(ChangeCount));
        OnPropertyChanged(nameof(ConflictCount));
        OnPropertyChanged(nameof(HasConflicts));
        OnPropertyChanged(nameof(HasChanges));
        _changed();
    }

    /// <summary>
    /// The settings top to bottom with a heading wherever a section starts. The list is only rebuilt when the
    /// filter changes, never while someone types, so a row keeps its focus.
    /// </summary>
    private List<object> BuildItems()
    {
        var items = new List<object>();
        var open = new List<string>();
        foreach (var setting in _settings.Where(setting => _showUnchanged || setting.IsNotable))
        {
            var common = 0;
            while (common < open.Count && common < setting.Sections.Count
                && open[common].Equals(setting.Sections[common], StringComparison.OrdinalIgnoreCase))
                common++;
            open.RemoveRange(common, open.Count - common);
            for (var depth = common; depth < setting.Sections.Count; depth++)
            {
                items.Add(new SectionViewModel(setting.Sections[depth], depth));
                open.Add(setting.Sections[depth]);
            }
            items.Add(setting);
        }
        return items;
    }

    /// <summary>
    /// Rows arrive in package order followed by server-only settings. Ordering every setting and section by where it
    /// first appears keeps each section together, so it is shown once.
    /// </summary>
    private static List<SettingViewModel> GroupBySection(List<SettingViewModel> settings)
    {
        var order = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        List<int> Key(SettingViewModel setting)
        {
            var prefix = "";
            var key = new List<int>();
            foreach (var segment in setting.Sections.Append(setting.Key))
            {
                prefix += "\n" + segment;
                order.TryAdd(prefix, order.Count);
                key.Add(order[prefix]);
            }
            return key;
        }

        var keys = settings.ToDictionary(setting => setting, Key);
        return settings.Order(Comparer<SettingViewModel>.Create((a, b) => Compare(keys[a], keys[b]))).ToList();

        static int Compare(List<int> a, List<int> b)
        {
            for (var i = 0; i < Math.Min(a.Count, b.Count); i++)
                if (a[i] != b[i])
                    return a[i].CompareTo(b[i]);
            return a.Count.CompareTo(b.Count);
        }
    }
}
