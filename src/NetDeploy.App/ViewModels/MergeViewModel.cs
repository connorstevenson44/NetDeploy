using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetDeploy.Core.Config;

namespace NetDeploy.App.ViewModels;

public sealed record ChoiceOption(MergeChoice Value, string Label);

public enum DiffLineKind { Context, Section, Removed, Added, Kept, Conflict, Controls, Note }

/// <summary>One line of the file view: the file as it will be written, with changes marked like a GitHub diff.</summary>
public sealed record DiffLine(DiffLineKind Kind, int Indent, string Text, MergeRowViewModel? Row = null, string? Label = null)
{
    public Thickness IndentMargin => new(Indent * 20, 0, 0, 0);
}

/// <summary>One setting. Changes go straight to the underlying MergeRow.</summary>
public sealed class MergeRowViewModel : ObservableObject
{
    private readonly MergeRow _row;
    private readonly IConfigFormat _format;
    private readonly Action _changed;
    private readonly IReadOnlyList<string> _segments;

    public MergeRowViewModel(MergeRow row, IConfigFormat format, bool isOverride, Action changed)
    {
        _row = row;
        _format = format;
        _changed = changed;
        _segments = format.SplitPath(row.Path);
        (CurrentLabel, IncomingLabel) = isOverride ? ("In the override", "Suggested") : ("Server", "Package");
        Choices =
        [
            .. row.Current is null ? [] : new[] { new ChoiceOption(MergeChoice.KeepCurrent, isOverride ? "Keep" : "Keep server value") },
            .. row.Incoming is null ? [] : new[] { new ChoiceOption(MergeChoice.TakeIncoming, isOverride ? "Apply" : "Use package value") },
            new(MergeChoice.Edit, "Edit"),
            new(MergeChoice.Remove, "Remove"),
        ];
    }

    public string Path => _row.Path;
    public IEnumerable<string> Sections => _segments.Take(_segments.Count - 1);
    public string Key => _segments[^1];
    public string Reason => _row.Reason;
    public bool IsChanged => _row.IsChanged;
    public bool NeedsChoice => _row.Choice is null;
    public string CurrentLabel { get; }
    public string IncomingLabel { get; }
    public IReadOnlyList<ChoiceOption> Choices { get; }
    public bool IsEditing => _row.Choice == MergeChoice.Edit;

    public MergeChoice? Choice
    {
        get => _row.Choice;
        set
        {
            // A chip list can push null while its items change; every real choice has a value.
            if (value is null || value == _row.Choice)
                return;

            // Start an edit from what the row shows now, so picking "Edit" alone changes nothing.
            if (value == MergeChoice.Edit && _row.EditedValue is null)
                _row.EditedValue = _row.Choice is null ? _row.Current ?? _row.Incoming : _row.Result ?? _row.Current ?? _row.Incoming;
            _row.Choice = value;
            _changed();
        }
    }

    /// <summary>The edited value as a person types it.</summary>
    public string EditText
    {
        get => _row.EditedValue is { } value ? _format.Display(value) : "";
        set
        {
            _row.EditedValue = _format.ParseEdit(_row.Current ?? _row.Incoming ?? "\"\"", value);
            _row.Choice = MergeChoice.Edit;
            _changed();
        }
    }

    public IEnumerable<DiffLine> Lines(int indent)
    {
        if (!IsChanged && !NeedsChoice)
        {
            yield return new(DiffLineKind.Context, indent, _format.Line(Key, _row.Result!));
            yield break;
        }

        if (NeedsChoice)
        {
            if (_row.Current is { } current)
                yield return new(DiffLineKind.Conflict, indent, _format.Line(Key, current), this, CurrentLabel);
            if (_row.Incoming is { } incoming)
                yield return new(DiffLineKind.Conflict, indent, _format.Line(Key, incoming), this, IncomingLabel);
        }
        else if (_row.Result is { } kept && kept == _row.Current)
        {
            yield return new(DiffLineKind.Kept, indent, _format.Line(Key, kept), this,
                _row.Incoming is null ? $"{IncomingLabel}: not set" : $"{IncomingLabel}: {_format.Display(_row.Incoming)}");
        }
        else
        {
            if (_row.Current is { } removed)
                yield return new(DiffLineKind.Removed, indent, _format.Line(Key, removed), this);
            if (_row.Result is { } added)
                yield return new(DiffLineKind.Added, indent, _format.Line(Key, added), this);
        }
        yield return new(DiffLineKind.Controls, indent, Reason, this);
    }
}

/// <summary>One config file in the review.</summary>
public sealed partial class MergeFileViewModel : ObservableObject
{
    private readonly ConfigFileUpdate _update;
    private readonly Action _changed;
    private bool _showUnchanged;

    public MergeFileViewModel(ConfigFileUpdate update, Action changed)
    {
        _update = update;
        _changed = changed;
        var format = update.Plan.Format;
        var isOverride = update.Kind == ConfigFileKind.Override;
        Rows = GroupBySection(update.Plan.Rows.Select(row => new MergeRowViewModel(row, format, isOverride, OnRowChanged)).ToList());
        Lines = BuildLines();
    }

    public string FileName => _update.FileName;
    public string Folder => Path.GetDirectoryName(_update.TargetPath) ?? "";
    public bool NeedsReview => _update.NeedsReview;
    public IReadOnlyList<MergeRowViewModel> Rows { get; }
    public int ChangeCount => Rows.Count(row => row.IsChanged);
    public int ConflictCount => Rows.Count(row => row.NeedsChoice);
    public bool HasConflicts => ConflictCount > 0;

    public string KindText => _update.Kind switch
    {
        ConfigFileKind.New => "New on this server",
        ConfigFileKind.Override => "Server override, checked against the new appsettings.json",
        _ => "Package file merged with the server's copy",
    };

    public string StatusText => ConflictCount > 0
        ? $"{ConflictCount} need{(ConflictCount == 1 ? "s" : "")} a choice"
        : ChangeCount > 0 ? $"{ChangeCount} change{(ChangeCount == 1 ? "" : "s")}" : "No changes";

    [ObservableProperty]
    public partial IReadOnlyList<DiffLine> Lines { get; private set; }

    public void ShowUnchanged(bool show)
    {
        _showUnchanged = show;
        Lines = BuildLines();
    }

    [RelayCommand]
    private void KeepAllServer() => SetAll(MergeChoice.KeepCurrent);

    [RelayCommand]
    private void UseAllPackage() => SetAll(MergeChoice.TakeIncoming);

    private void SetAll(MergeChoice choice)
    {
        foreach (var row in Rows.Where(row => row.IsChanged && row.Choices.Any(option => option.Value == choice)))
            row.Choice = choice;
    }

    private void OnRowChanged()
    {
        Lines = BuildLines();
        OnPropertyChanged(nameof(ChangeCount));
        OnPropertyChanged(nameof(ConflictCount));
        OnPropertyChanged(nameof(HasConflicts));
        OnPropertyChanged(nameof(StatusText));
        _changed();
    }

    /// <summary>The file from top to bottom, opening and closing sections around the settings shown.</summary>
    private List<DiffLine> BuildLines()
    {
        var format = _update.Plan.Format;
        var visible = Rows.Where(row => _showUnchanged || row.IsChanged || row.NeedsChoice).ToList();
        if (visible.Count == 0)
            return [new(DiffLineKind.Note, 0, "Nothing changes in this file. Tick \"Show unchanged settings\" to see all of it.")];

        var lines = new List<DiffLine> { new(DiffLineKind.Section, 0, format.OpenSection(null)) };
        var open = new List<string>();
        foreach (var row in visible)
        {
            var sections = row.Sections.ToList();
            var common = 0;
            while (common < open.Count && common < sections.Count && open[common].Equals(sections[common], StringComparison.OrdinalIgnoreCase))
                common++;
            for (var i = open.Count - 1; i >= common; i--)
                lines.Add(new(DiffLineKind.Section, i + 1, format.CloseSection(open[i])));
            open.RemoveRange(common, open.Count - common);
            for (var i = common; i < sections.Count; i++)
            {
                lines.Add(new(DiffLineKind.Section, i + 1, format.OpenSection(sections[i])));
                open.Add(sections[i]);
            }
            lines.AddRange(row.Lines(sections.Count + 1));
        }
        for (var i = open.Count - 1; i >= 0; i--)
            lines.Add(new(DiffLineKind.Section, i + 1, format.CloseSection(open[i])));
        lines.Add(new(DiffLineKind.Section, 0, format.CloseSection(null)));
        return lines;
    }

    /// <summary>
    /// Rows arrive in package order followed by server-only settings. Ordering every setting and section by where it
    /// first appears keeps each section together, so it is shown once.
    /// </summary>
    private static List<MergeRowViewModel> GroupBySection(List<MergeRowViewModel> rows)
    {
        var order = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        List<int> Key(MergeRowViewModel row)
        {
            var prefix = "";
            var key = new List<int>();
            foreach (var segment in row.Sections.Append(row.Key))
            {
                prefix += "\n" + segment;
                order.TryAdd(prefix, order.Count);
                key.Add(order[prefix]);
            }
            return key;
        }

        var keys = rows.ToDictionary(row => row, Key);
        return rows.Order(Comparer<MergeRowViewModel>.Create((a, b) => Compare(keys[a], keys[b]))).ToList();

        static int Compare(List<int> a, List<int> b)
        {
            for (var i = 0; i < Math.Min(a.Count, b.Count); i++)
                if (a[i] != b[i])
                    return a[i].CompareTo(b[i]);
            return a.Count.CompareTo(b.Count);
        }
    }
}

/// <summary>The config review: every config file the deploy will write.</summary>
public sealed partial class MergeViewModel : ObservableObject
{
    public MergeViewModel(IReadOnlyList<ConfigFileUpdate> files, bool isReadOnly)
    {
        IsReadOnly = isReadOnly;
        Files = files.Select(file => new MergeFileViewModel(file, OnChanged)).ToList();
        SelectedFile = Files.FirstOrDefault(file => file.ConflictCount > 0) ?? Files.FirstOrDefault(file => file.NeedsReview) ?? Files[0];
    }

    /// <summary>True in a dry run: the review shows the merge but nothing is written.</summary>
    public bool IsReadOnly { get; }

    public bool IsEditable => !IsReadOnly;
    public IReadOnlyList<MergeFileViewModel> Files { get; }
    public string ApplyText => IsReadOnly ? "Continue dry run" : "Write config files";
    public bool CanApply => IsReadOnly || Files.All(file => file.ConflictCount == 0);
    public string Title => $"{Files.Count} config file{(Files.Count == 1 ? "" : "s")} to review";

    public string Summary
    {
        get
        {
            var changes = Files.Sum(file => file.ChangeCount);
            var conflicts = Files.Sum(file => file.ConflictCount);
            return conflicts > 0
                ? $"{changes} changes. {conflicts} need a choice before the files can be written."
                : $"{changes} changes. Check them, then write the files.";
        }
    }

    [ObservableProperty]
    public partial MergeFileViewModel SelectedFile { get; set; }

    [ObservableProperty]
    public partial bool ShowUnchanged { get; set; }

    partial void OnShowUnchangedChanged(bool value)
    {
        foreach (var file in Files)
            file.ShowUnchanged(value);
    }

    private void OnChanged()
    {
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(CanApply));
    }
}
