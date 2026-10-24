using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetDeploy.Core.Config;

namespace NetDeploy.App.ViewModels;

/// <summary>What will happen to a setting, shown as an icon and a colour.</summary>
public enum SettingStatus { Unchanged, Added, Removed, Changed, Kept, Edited, NeedsValue }

/// <summary>
/// A line of the side-by-side diff, with the file now on the left and the file after the change on the right. A line
/// can be on one side only, such as a setting the package adds. ConfigFileViewModel works out the numbers, the
/// separators and which side each line is on, again after every change, so both sides read like the real files.
/// </summary>
public abstract partial class DiffLine(int depth) : ObservableObject
{
    public Thickness Indent { get; } = new(depth * 16, 0, 0, 0);

    /// <summary>The line's number in the file now, or null when the line isn't in it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOnLeft))]
    public partial int? LeftNumber { get; internal set; }

    /// <summary>The line's number in the file after the change, or null when the line isn't in it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOnRight))]
    public partial int? RightNumber { get; internal set; }

    /// <summary>The comma after the line on the left, in JSON, when something follows it.</summary>
    [ObservableProperty]
    public partial string LeftSeparator { get; internal set; } = "";

    [ObservableProperty]
    public partial string RightSeparator { get; internal set; } = "";

    public bool IsOnLeft => LeftNumber is not null;
    public bool IsOnRight => RightNumber is not null;

    internal bool PlacedLeft { get; private set; }
    internal bool PlacedRight { get; private set; }

    /// <summary>Says which sides the line is on; the numbers follow from it.</summary>
    internal void Place(bool left, bool right) => (PlacedLeft, PlacedRight) = (left, right);
}

/// <summary>The line that opens or closes a section, such as <c>"Logging": {</c> or <c>&lt;/appSettings&gt;</c>.</summary>
public sealed class SectionLine(string? name, string text, bool isClosing, int depth) : DiffLine(depth)
{
    /// <summary>The section's name; null for the file's root.</summary>
    public string? Name { get; } = name;

    public string Text { get; } = text;
    public bool IsClosing { get; } = isClosing;
}

/// <summary>Stands for unchanged lines that are hidden, like the folded regions of a diff.</summary>
public sealed class GapLine(int count) : DiffLine(0)
{
    public int Count { get; } = count;
    public string Text => Count == 1 ? "1 unchanged line" : $"{Count} unchanged lines";
}

/// <summary>
/// One setting, side by side: the value now on the left (read-only) and the value that will be written on the
/// right (editable). Changes go straight to the underlying MergeRow.
/// </summary>
public sealed partial class SettingViewModel : DiffLine
{
    private readonly MergeRow _row;
    private readonly IConfigFormat _format;
    private readonly Action _changed;
    private bool _editedByUser;

    public SettingViewModel(MergeRow row, IConfigFormat format, Action changed) : this(row, format, format.SplitPath(row.Path), changed)
    {
    }

    // A setting sits one level inside its innermost section, and the file's root is level 0.
    private SettingViewModel(MergeRow row, IConfigFormat format, IReadOnlyList<string> segments, Action changed) : base(segments.Count)
    {
        _row = row;
        _format = format;
        _changed = changed;
        Sections = segments.Take(segments.Count - 1).ToList();
        Key = segments[^1];
    }

    public string Path => _row.Path;
    public IReadOnlyList<string> Sections { get; }
    public string Key { get; }

    /// <summary>The setting's line in the file now, as it is written there.</summary>
    public string? NowLine => _row.Current is { } current ? _format.Line(Key, current) : null;

    /// <summary>The text of the line after the change, before and after the editable value.</summary>
    public string Before => Around.Before;
    public string After => Around.After;

    private (string Before, string After) Around => _format.Around(Key, Result ?? _row.Incoming ?? _row.Current ?? "\"\"");

    /// <summary>True when the setting is in the file now.</summary>
    internal bool InFileNow => _row.Current is not null;

    /// <summary>True when the setting will be in the file after the change. One that needs a value shows, empty.</summary>
    internal bool InFileAfter => NeedsValue || _row.Result is not null;

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
                _editedByUser = true;
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
        SettingStatus.Edited => _editedByUser ? "Your value." : _row.Reason + ".",
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

/// <summary>
/// One config file as a side-by-side diff: the file now on the left and the file after the change on the right, line
/// by line, with every setting editable in place on the right.
/// </summary>
public sealed partial class ConfigFileViewModel : ObservableObject
{
    private readonly ConfigFileUpdate _update;
    private readonly Action _changed;
    private readonly List<SettingViewModel> _settings;
    private readonly Section _root;
    private readonly List<DiffLine> _lines;
    private bool _showUnchanged;
    private bool _batching;

    public ConfigFileViewModel(ConfigFileUpdate update, Action changed)
    {
        _update = update;
        _changed = changed;
        var format = update.Plan.Format;
        _settings = GroupBySection(update.Plan.Rows.Select(row => new SettingViewModel(row, format, OnSettingChanged)).ToList());
        _root = Section.Build(_settings, format);
        _lines = _root.Lines().ToList();
        Layout();
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
    public string ConflictText => ConflictCount == 1 ? "1 setting needs a value" : $"{ConflictCount} settings need a value";
    public bool HasChanges => ChangeCount > 0;

    /// <summary>Lines only on the right, lines only on the left, and settings on both that change, for the totals.</summary>
    public int AddedCount => _settings.Count(setting => setting.Status == SettingStatus.Added);
    public int RemovedCount => _settings.Count(setting => setting.Status == SettingStatus.Removed);
    public int ModifiedCount => _settings.Count(setting => setting.Status is SettingStatus.Changed or SettingStatus.Edited);

    public string KindTip => _update.Kind switch
    {
        ConfigFileKind.New => "New file, the server has no copy yet",
        ConfigFileKind.Override => "Server-only override, checked against the new appsettings.json",
        ConfigFileKind.Edit => "Your edits to this file",
        _ => "The server's copy merged with the package's",
    };

    /// <summary>Headings over the two sides: the left side is always what is there now.</summary>
    public string NowHeading => _update.Kind != ConfigFileKind.New ? "Now"
        : _settings.Any(setting => setting.InFileNow) ? "Package defaults (no file yet)" : "Now (no file yet)";
    public string ValueHeading => _update.Kind == ConfigFileKind.Edit ? "With your changes" : "After deploy";
    public string ResetTip => _update.Kind == ConfigFileKind.Edit ? "Undo" : "Use the package value";

    /// <summary>The lines to show, in file order, with hidden unchanged lines folded into gaps.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<DiffLine> Items { get; private set; }

    public void ShowUnchanged(bool show)
    {
        _showUnchanged = show;
        Items = BuildItems();
    }

    [RelayCommand]
    private void KeepAllNow() => Batch(() =>
    {
        foreach (var setting in _settings.Where(setting => setting.IsNotable && setting.CanKeepNow))
            setting.KeepNowCommand.Execute(null);
    });

    [RelayCommand]
    private void ResetAll() => Batch(() =>
    {
        foreach (var setting in _settings.Where(setting => setting.CanReset))
            setting.ResetCommand.Execute(null);
    });

    // Lays the lines out once for a change to many settings, rather than once per setting.
    private void Batch(Action change)
    {
        _batching = true;
        try
        {
            change();
        }
        finally
        {
            _batching = false;
        }
        OnSettingChanged();
    }

    private void OnSettingChanged()
    {
        if (_batching)
            return;
        Layout();
        OnPropertyChanged(nameof(ChangeCount));
        OnPropertyChanged(nameof(ConflictCount));
        OnPropertyChanged(nameof(HasConflicts));
        OnPropertyChanged(nameof(ConflictText));
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(AddedCount));
        OnPropertyChanged(nameof(RemovedCount));
        OnPropertyChanged(nameof(ModifiedCount));
        _changed();
    }

    /// <summary>
    /// Works out which side each line is on, its separator and its number on each side. It changes the lines in
    /// place and never rebuilds the list, so a value being typed keeps its focus.
    /// </summary>
    private void Layout()
    {
        // A new file has no left side, except the defaults of settings filled from the deploy's settings.
        _root.Place(fileExistsNow: _update.Kind == ConfigFileKind.New ? null : true);
        int left = 0, right = 0;
        foreach (var line in _lines)
        {
            line.LeftNumber = line.PlacedLeft ? ++left : null;
            line.RightNumber = line.PlacedRight ? ++right : null;
        }
    }

    /// <summary>
    /// Every line, or with unchanged settings hidden, the changed ones inside the sections that hold them and a gap
    /// for each run of hidden lines. The list is only rebuilt when the filter changes, never while someone types.
    /// </summary>
    private List<DiffLine> BuildItems()
    {
        if (_showUnchanged)
            return [.. _lines];
        if (!_settings.Any(setting => setting.IsNotable))
            return [];

        var shown = _root.Shown();
        var items = new List<DiffLine>();
        var hidden = 0;
        foreach (var line in _lines)
        {
            if (!shown.Contains(line))
            {
                hidden++;
                continue;
            }
            if (hidden > 0)
                items.Add(new GapLine(hidden));
            hidden = 0;
            items.Add(line);
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

    /// <summary>A section of the file and what is in it, in order: its settings and the sections inside it.</summary>
    private sealed class Section
    {
        private readonly List<object> _children = [];
        private readonly IConfigFormat _format;

        private Section(string? name, int depth, IConfigFormat format)
        {
            _format = format;
            Name = name;
            Open = new SectionLine(name, format.OpenSection(name), isClosing: false, depth);
            Close = new SectionLine(name, format.CloseSection(name), isClosing: true, depth);
        }

        public string? Name { get; }
        public SectionLine Open { get; }
        public SectionLine Close { get; }

        /// <summary>Nests settings, already grouped by section, under the file's root.</summary>
        public static Section Build(IEnumerable<SettingViewModel> settings, IConfigFormat format)
        {
            var root = new Section(null, 0, format);
            var open = new List<Section> { root };
            foreach (var setting in settings)
            {
                var common = 0;
                while (common + 1 < open.Count && common < setting.Sections.Count
                    && open[common + 1].Name!.Equals(setting.Sections[common], StringComparison.OrdinalIgnoreCase))
                    common++;
                open.RemoveRange(common + 1, open.Count - common - 1);
                for (var depth = common; depth < setting.Sections.Count; depth++)
                {
                    var section = new Section(setting.Sections[depth], depth + 1, format);
                    open[^1]._children.Add(section);
                    open.Add(section);
                }
                open[^1]._children.Add(setting);
            }
            return root;
        }

        public IEnumerable<DiffLine> Lines()
        {
            yield return Open;
            foreach (var child in _children)
            {
                if (child is Section section)
                    foreach (var line in section.Lines())
                        yield return line;
                else
                    yield return (SettingViewModel)child;
            }
            yield return Close;
        }

        /// <summary>
        /// Puts the section on each side that has something in it, and gives each child a separator on the sides
        /// where something follows it. The root is always on the right, and on the left when the file is there now.
        /// </summary>
        public (bool Left, bool Right) Place(bool? fileExistsNow = null)
        {
            var sides = _children.Select(child => child is Section section ? section.Place() : Sides((SettingViewModel)child)).ToList();
            var left = fileExistsNow ?? sides.Any(side => side.Left);
            var right = fileExistsNow is not null || sides.Any(side => side.Right);
            var lastLeft = sides.FindLastIndex(side => side.Left);
            var lastRight = sides.FindLastIndex(side => side.Right);
            for (var i = 0; i < _children.Count; i++)
            {
                DiffLine end = _children[i] is Section section ? section.Close : (SettingViewModel)_children[i];
                end.LeftSeparator = i < lastLeft && sides[i].Left ? _format.Separator : "";
                end.RightSeparator = i < lastRight && sides[i].Right ? _format.Separator : "";
            }
            Open.Place(left, right);
            Close.Place(left, right);
            return (left, right);
        }

        private static (bool Left, bool Right) Sides(SettingViewModel setting)
        {
            setting.Place(setting.InFileNow, setting.InFileAfter);
            return (setting.InFileNow, setting.InFileAfter);
        }

        /// <summary>The changed settings and the opening and closing lines of the sections around them.</summary>
        public HashSet<DiffLine> Shown()
        {
            var shown = new HashSet<DiffLine>();
            AddShown(shown);
            shown.Add(Open);
            shown.Add(Close);
            return shown;
        }

        private bool AddShown(HashSet<DiffLine> shown)
        {
            var any = false;
            foreach (var child in _children)
            {
                if (child is Section section ? section.AddShown(shown) : ((SettingViewModel)child).IsNotable && shown.Add((SettingViewModel)child))
                    any = true;
            }
            if (any)
            {
                shown.Add(Open);
                shown.Add(Close);
            }
            return any;
        }
    }
}
