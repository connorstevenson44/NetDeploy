using System.IO;
using System.Text.Json;
using System.Windows.Input;
using System.Xml;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MaterialDesignThemes.Wpf;
using NetDeploy.Core.Config;
using NetDeploy.Core.Text;

namespace NetDeploy.App.ViewModels;

/// <summary>What a mark in the editor stands for.</summary>
public enum MarkKind
{
    /// <summary>Text that changes from the server's file: what was there shows struck out, what replaces it in green.</summary>
    Change,

    /// <summary>A setting that needs the user to pick its value, highlighted until they do.</summary>
    NeedsChoice,

    /// <summary>Text kept from the server's file where the package has something else, which can be taken instead.</summary>
    PackageDiffers,
}

/// <summary>A button on a mark, such as putting the server's text back.</summary>
public sealed record MarkAction(string Name, PackIconKind Icon, string Tip, ICommand Command);

/// <summary>
/// A stretch of the file in the editor that differs from the server's file, or needs a choice, with the buttons that
/// act on it. Start and Length are where it is in the text being edited; Removed is the server's text it replaces.
/// </summary>
public sealed record ChangeMark(MarkKind Kind, int Start, int Length, string Removed, string Note, IReadOnlyList<MarkAction> Actions)
{
    public int End => Start + Length;
}

/// <summary>A setting that needs a value, listed above the editor with the ways to settle it.</summary>
public sealed record SettingChoice(string Path, string Reason, IReadOnlyList<MarkAction> Actions);

/// <summary>
/// One file in a track-changes editor: the whole file as it will be written, editable as text, with what differs from
/// the server's file marked in it, the way a word processor tracks changes. Each change has buttons to put the server's
/// text back or, in a deploy, to take the package's. Settings files also follow the text setting by setting, so a
/// setting that needs a value is highlighted until it has one. Any other text file is edited the same way.
/// </summary>
public sealed partial class ConfigFileViewModel : ObservableObject
{
    private readonly ConfigFileUpdate _update;
    private readonly Action _changed;
    private string _text;

    public ConfigFileViewModel(ConfigFileUpdate update, Action changed)
    {
        _update = update;
        _changed = changed;
        _text = update.Plan.EditedText ?? update.Plan.BuildText();
        Refresh();
    }

    public ConfigFileUpdate Update => _update;
    public string FileName => _update.FileName;
    public string Folder => Path.GetDirectoryName(_update.TargetPath) ?? "";
    public ConfigFileKind Kind => _update.Kind;
    public bool NeedsReview => _update.NeedsReview;

    /// <summary>The server's file, which the marks compare against; null when the server has none.</summary>
    public string? ServerText => _update.Plan.CurrentText;

    /// <summary>The package's file, in a deploy that merges it with the server's.</summary>
    public string? PackageText => _update.Kind == ConfigFileKind.Update ? _update.PackageText : null;

    public string KindTip => _update.Kind switch
    {
        ConfigFileKind.New => "New file, the server has no copy yet",
        ConfigFileKind.Override => "Server-only override, checked against the new appsettings.json",
        ConfigFileKind.Edit => "Your edits to this file",
        _ => "The server's copy merged with the package's",
    };

    /// <summary>The whole file as it will be written. Every change to it is tracked against the server's file.</summary>
    public string Text
    {
        get => _text;
        set
        {
            if (!SetProperty(ref _text, value))
                return;
            try
            {
                _update.Plan.Edit(value);
                TextError = null;
            }
            catch (Exception ex) when (ex is JsonException or XmlException or InvalidDataException)
            {
                TextError = ex.Message;
            }
            Refresh();
            _changed();
        }
    }

    /// <summary>Why the text can't be read as settings, or null when it can.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTextError))]
    public partial string? TextError { get; private set; }

    public bool HasTextError => TextError is not null;

    /// <summary>The marks, in the order they are in the text.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<ChangeMark> Marks { get; private set; } = [];

    /// <summary>Settings that still need a value, for the list above the editor.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<SettingChoice> Choices { get; private set; } = [];

    public int ChangeCount => Marks.Count(mark => mark.Kind == MarkKind.Change);
    public bool HasChanges => ChangeCount > 0;
    public int ConflictCount => _update.Plan.ConflictCount;
    public bool HasConflicts => ConflictCount > 0;
    public string ConflictText => ConflictCount == 1 ? "1 setting needs a value" : $"{ConflictCount} settings need a value";

    /// <summary>Changes that only add text, only take text out, or replace it, for the totals over the editor.</summary>
    public int AddedCount => Marks.Count(mark => mark.Kind == MarkKind.Change && mark.Removed.Length == 0);
    public int RemovedCount => Marks.Count(mark => mark.Kind == MarkKind.Change && mark.Length == 0);
    public int ModifiedCount => ChangeCount - AddedCount - RemovedCount;

    public string UndoAllTip => _update.Kind == ConfigFileKind.Edit
        ? "Undo every change: back to the file as it is on the server"
        : "Undo your changes: back to the merge NetDeploy worked out";

    /// <summary>Puts the text back as it was before the user changed anything.</summary>
    [RelayCommand]
    private void UndoAll()
    {
        _update.Plan.CancelTextEdit();
        _text = _update.Plan.BuildText();
        OnPropertyChanged(nameof(Text));
        TextError = null;
        Refresh();
        _changed();
    }

    /// <summary>Replaces a stretch of the text, as the buttons on the marks do.</summary>
    private void Replace(int start, int length, string with) => Text = _text[..start] + with + _text[(start + length)..];

    /// <summary>Works out the marks and totals again from the text.</summary>
    private void Refresh()
    {
        var marks = new List<ChangeMark>();
        if (ServerText is { } server)
            AddChanges(marks, server);
        var choices = TextError is null ? AddChoices(marks) : [];
        Marks = marks.OrderBy(mark => mark.Start).ThenBy(mark => mark.Kind).ToList();
        Choices = choices;
        OnPropertyChanged(nameof(ChangeCount));
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(ConflictCount));
        OnPropertyChanged(nameof(HasConflicts));
        OnPropertyChanged(nameof(ConflictText));
        OnPropertyChanged(nameof(AddedCount));
        OnPropertyChanged(nameof(RemovedCount));
        OnPropertyChanged(nameof(ModifiedCount));
    }

    // What differs from the server's file, and in a deploy, where the text keeps the server's text but the package
    // has other text.
    private void AddChanges(List<ChangeMark> marks, string server)
    {
        var changes = TextDiff.Compare(server, _text);
        var package = PackageText;
        var toPackage = package is null ? null : TextDiff.Compare(server, package);
        foreach (var change in changes)
        {
            var removed = server.Substring(change.OldStart, change.OldLength);
            var added = _text.Substring(change.NewStart, change.NewLength);
            var actions = new List<MarkAction>
            {
                new(_update.Kind == ConfigFileKind.Edit ? "Undo" : "Keep the server's", PackIconKind.Undo,
                    removed.Length == 0 ? "Take this out again: the server's file doesn't have it" : $"Put back the server's text: {Short(removed)}",
                    new RelayCommand(() => Replace(change.NewStart, change.NewLength, removed))),
            };
            if (toPackage is not null && PackageStretch(toPackage, package!, change.OldStart, change.OldEnd) is { } theirs && theirs != added && theirs != removed)
                actions.Add(UsePackage(theirs, () => Replace(change.NewStart, change.NewLength, theirs)));
            marks.Add(new ChangeMark(MarkKind.Change, change.NewStart, change.NewLength, removed, "", actions));
        }

        if (toPackage is null)
            return;
        foreach (var difference in toPackage)
        {
            // Only where the text still has the server's text: the changes above cover the rest.
            if (changes.Any(change => change.OldStart <= difference.OldEnd && difference.OldStart <= change.OldEnd))
                continue;
            var start = TextDiff.Map(changes, difference.OldStart);
            var end = TextDiff.Map(changes, difference.OldEnd, atEnd: true);
            var theirs = package!.Substring(difference.NewStart, difference.NewLength);
            marks.Add(new ChangeMark(MarkKind.PackageDiffers, start, end - start, "", $"The package has: {Short(theirs)}",
                [UsePackage(theirs, () => Replace(start, end - start, theirs))]));
        }
    }

    // Highlights each setting that needs a value, with buttons to settle it.
    private List<SettingChoice> AddChoices(List<ChangeMark> marks)
    {
        var plan = _update.Plan;
        var unsettled = plan.Rows.Where(row => row.Choice is null).ToList();
        if (unsettled.Count == 0)
            return [];
        var spans = plan.Format.Locate(_text);
        var choices = new List<SettingChoice>();
        foreach (var row in unsettled)
        {
            var actions = new List<MarkAction>();
            if (row.Current is { } current)
                actions.Add(new MarkAction("Keep the server's", PackIconKind.Server, $"Keep the server's value for {row.Path}: {Short(plan.Format.Display(current))}",
                    new RelayCommand(() => Choose(row, MergeChoice.KeepCurrent))));
            if (row.Incoming is { } incoming)
                actions.Add(UsePackage(plan.Format.Display(incoming), () => Choose(row, MergeChoice.TakeIncoming), $"for {row.Path}"));
            else
                actions.Add(new MarkAction("Remove", PackIconKind.Close, $"Take {row.Path} out of the file",
                    new RelayCommand(() => Choose(row, MergeChoice.Remove))));
            choices.Add(new SettingChoice(row.Path, row.Reason, actions));
            if (spans.TryGetValue(row.Path, out var span))
                marks.Add(new ChangeMark(MarkKind.NeedsChoice, span.Start, span.End - span.Start, "", $"{row.Path}: {row.Reason}", actions));
        }
        return choices;
    }

    private void Choose(MergeRow row, MergeChoice choice)
    {
        _text = _update.Plan.Choose(row, choice);
        OnPropertyChanged(nameof(Text));
        TextError = null;
        Refresh();
        _changed();
    }

    private static MarkAction UsePackage(string theirs, Action use, string what = "") =>
        new("Use the package's", PackIconKind.PackageVariantClosed,
            $"Use the package's {(what.Length > 0 ? "value " + what : "text")}: {(theirs.Length == 0 ? "nothing, it takes this out" : Short(theirs))}",
            new RelayCommand(use));

    /// <summary>
    /// The package's text for a stretch of the server's file, or null when the package changes text across its edges,
    /// so the stretch has no clean counterpart.
    /// </summary>
    private static string? PackageStretch(IReadOnlyList<TextChange> toPackage, string package, int start, int end)
    {
        if (toPackage.Any(difference => difference.OldStart < start && start < difference.OldEnd || difference.OldStart < end && end < difference.OldEnd))
            return null;
        var from = TextDiff.Map(toPackage, start);
        var to = TextDiff.Map(toPackage, end, atEnd: true);
        return package[from..to];
    }

    // Text for a tooltip: one line, cut short.
    private static string Short(string text)
    {
        var line = text.Trim().ReplaceLineEndings(" ↵ ");
        return line.Length <= 80 ? line : line[..77] + "…";
    }
}
