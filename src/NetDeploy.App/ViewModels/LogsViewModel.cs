using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MaterialDesignThemes.Wpf;
using NetDeploy.Core.Logs;

namespace NetDeploy.App.ViewModels;

/// <summary>A heading in the list of logs: the live ones, then the archived ones.</summary>
public sealed record LogSectionViewModel(string Title);

/// <summary>One log in the list, with how many matches a search of every log found in it.</summary>
public sealed partial class AppLogViewModel : ObservableObject
{
    public AppLogViewModel(AppLog log) => Log = log;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Detail), nameof(IsLive), nameof(Icon))]
    public partial AppLog Log { get; private set; }

    public string Name => Log.Name;
    public bool IsLive => Log.IsLive;
    public bool IsZipped => Log.IsZipped;

    /// <summary>The zip a zipped log is in, or null.</summary>
    public string? Zip => Log.IsZipped ? Path.GetFileName(Log.Path) : null;

    /// <summary>Where it is, for the tooltip: the file, or the zip and the file in it.</summary>
    public string Where => Log.IsZipped ? $"{Log.Path} › {Log.Entry}" : Log.Path;

    public PackIconKind Icon => IsZipped ? PackIconKind.FolderZipOutline : IsLive ? PackIconKind.FileDocumentEditOutline : PackIconKind.FileDocumentOutline;

    /// <summary>Such as "12 KB · 24 Oct, 10:15", and for a zipped log, the zip it is in.</summary>
    public string Detail => string.Join(" · ", new[]
    {
        Words.Size(Log.Size),
        Log.Modified.ToString("d MMM, HH:mm", System.Globalization.CultureInfo.CurrentCulture),
        Zip is { } zip ? "in " + zip : null,
    }.OfType<string>());

    /// <summary>The matches the last search of every log found in it; null when it wasn't searched.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHits), nameof(IsMiss), nameof(HitsText), nameof(AutomationName))]
    public partial int? Hits { get; set; }

    public bool HasHits => Hits > 0;

    /// <summary>Searched, with no matches: the row fades, so the logs with some stand out.</summary>
    public bool IsMiss => Hits == 0;

    public string HitsText => Hits is { } hits ? hits.ToString("N0", System.Globalization.CultureInfo.CurrentCulture) : "";

    /// <summary>The log open on the right.</summary>
    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    /// <summary>What a screen reader says for the row, such as "app.3.log in app.3.zip, 2 matches".</summary>
    public string AutomationName => Name + (Zip is { } zip ? " in " + zip : "") + Hits switch
    {
        null => "",
        0 => ", no matches",
        { } hits => ", " + Words.Count(hits, "match", "matches"),
    };

    internal void Update(AppLog log)
    {
        Log = log;
        OnPropertyChanged(nameof(Name));
    }
}

/// <summary>
/// The logs of an app whose nlog.config writes to files: the live log, followed as the app writes it, and the older and
/// archived ones, zipped ones too. A search highlights its matches in the log that is open, and searching every log
/// marks the ones with matches. Reading, searching and following all happen off the UI thread, so a big log or a slow
/// search never freezes the window.
/// </summary>
public sealed partial class LogsViewModel : ObservableObject, IDisposable
{
    /// <summary>The most matches highlighted in the open log.</summary>
    internal const int MaxMatches = 50_000;

    /// <summary>How often the list of logs is read again, in ticks of the timer that follows the live log.</summary>
    private const int ListEvery = 15;

    private readonly DispatcherTimer _timer;
    private readonly ZipContents _zips = new();
    private CancellationTokenSource? _loading;
    private CancellationTokenSource? _finding;
    private CancellationTokenSource? _searching;
    private LogTail? _tail;
    private string? _openKey;
    private string _text = "";
    private Regex? _pattern;
    private bool _following;
    private int _ticks;
    private string? _searchResult;

    public LogsViewModel(string configPath)
    {
        ConfigPath = configPath;
        Folder = Path.GetDirectoryName(configPath)!;
        // The live log is checked for new lines every second.
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Tick(), Dispatcher.CurrentDispatcher);
        Listing = ListAsync();
        _timer.Start();
    }

    /// <summary>The app's nlog.config.</summary>
    public string ConfigPath { get; }

    /// <summary>The app's folder, which ${basedir} stands for.</summary>
    public string Folder { get; }

    /// <summary>Headings and logs: the live logs, then the rest newest first.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLogs))]
    public partial IReadOnlyList<object> Rows { get; private set; } = [];

    public IEnumerable<AppLogViewModel> Logs => Rows.OfType<AppLogViewModel>();

    public bool HasLogs => Rows.Count > 0;

    /// <summary>Why there are no logs to show, such as nlog.config not being readable or no log written yet.</summary>
    [ObservableProperty]
    public partial string? Problem { get; private set; }

    /// <summary>The log open on the right.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOpen))]
    public partial AppLogViewModel? Open { get; private set; }

    public bool HasOpen => Open is not null;

    /// <summary>The open log's text, or its end for a long one. It grows as the app writes; Appended says by how much.</summary>
    public string Text => _text;

    /// <summary>Text the app has just added to the open log, for the view to add without showing it all again.</summary>
    public event EventHandler<string>? Appended;

    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    /// <summary>Above the text: that only the end of a long log is shown, or why it can't be read.</summary>
    [ObservableProperty]
    public partial string? Note { get; private set; }

    /// <summary>Keeps the newest lines in view as the app writes them. Scrolling up turns it off; Follow turns it on.</summary>
    [ObservableProperty]
    public partial bool Follow { get; set; } = true;

    // ===== Searching the open log =====

    /// <summary>What to search for: the text as typed, or with IsRegex, a regular expression.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MatchText))]
    public partial string Query { get; set; } = "";

    [ObservableProperty]
    public partial bool IsRegex { get; set; }

    [ObservableProperty]
    public partial bool MatchCase { get; set; }

    /// <summary>Why the query can't be searched for, such as a regular expression that isn't valid.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchAllCommand))]
    public partial string? QueryError { get; private set; }

    /// <summary>The matches in the open log, in order, to highlight.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MatchText), nameof(CurrentMatch))]
    [NotifyCanExecuteChangedFor(nameof(NextMatchCommand), nameof(PreviousMatchCommand))]
    public partial IReadOnlyList<LogMatch> Matches { get; private set; } = [];

    /// <summary>The match the view shows, by its place in Matches; -1 for none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MatchText), nameof(CurrentMatch))]
    public partial int Current { get; private set; } = -1;

    public LogMatch? CurrentMatch => Current >= 0 && Current < Matches.Count ? Matches[Current] : null;

    /// <summary>Such as "3 of 12", or "No matches".</summary>
    public string MatchText
    {
        get
        {
            if (_pattern is null)
                return "";
            if (Matches.Count == 0)
                return "No matches";
            var total = Matches.Count.ToString("N0", System.Globalization.CultureInfo.CurrentCulture) + (Matches.Count >= MaxMatches ? "+" : "");
            return Current < 0 ? $"{total} matches" : $"{Current + 1:N0} of {total}";
        }
    }

    // ===== Searching every log =====

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SearchStatus))]
    [NotifyCanExecuteChangedFor(nameof(SearchAllCommand), nameof(CancelSearchCommand))]
    public partial bool IsSearching { get; private set; }

    /// <summary>How far the search of every log has got, from 0 to 100.</summary>
    [ObservableProperty]
    public partial double SearchProgress { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SearchStatus))]
    public partial int Searched { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SearchStatus))]
    public partial int SearchTotal { get; private set; }

    /// <summary>Such as "Searching 3 of 12 logs…" or "Found in 2 of 12 logs".</summary>
    public string? SearchStatus => IsSearching ? $"Searching {Searched:N0} of {Words.Count(SearchTotal, "log", "logs")}…" : _searchResult;

    /// <summary>The listing of the logs in progress, for tests to wait on.</summary>
    public Task Listing { get; private set; }

    /// <summary>The reading of the open log in progress, for tests to wait on.</summary>
    public Task Loading { get; private set; } = Task.CompletedTask;

    /// <summary>The search of the open log in progress, for tests to wait on.</summary>
    public Task Finding { get; private set; } = Task.CompletedTask;

    /// <summary>The search of every log in progress, for tests to wait on.</summary>
    public Task Searching { get; private set; } = Task.CompletedTask;

    [RelayCommand]
    private void OpenLog(AppLogViewModel log)
    {
        if (log.Log.Key != _openKey)
            Show(log);
    }

    /// <summary>Lists the logs again, and reads the open one again from the start.</summary>
    [RelayCommand]
    private void Refresh()
    {
        _openKey = null;
        Listing = ListAsync();
    }

    [RelayCommand(CanExecute = nameof(HasMatches))]
    private void NextMatch() => GoToMatch(1);

    [RelayCommand(CanExecute = nameof(HasMatches))]
    private void PreviousMatch() => GoToMatch(-1);

    private bool HasMatches() => Matches.Count > 0;

    private void GoToMatch(int direction)
    {
        Follow = false;
        Current = Current < 0 ? (direction > 0 ? 0 : Matches.Count - 1) : (Current + direction + Matches.Count) % Matches.Count;
    }

    private bool CanSearchAll() => !IsSearching && _pattern is not null && HasLogs;

    /// <summary>Searches every log, marking each with how many matches it has as it goes. It can be stopped part way.</summary>
    [RelayCommand(CanExecute = nameof(CanSearchAll))]
    private void SearchAll() => Searching = SearchAllAsync();

    [RelayCommand(CanExecute = nameof(IsSearching))]
    private void CancelSearch() => _searching?.Cancel();

    partial void OnQueryChanged(string value) => OnPatternChanged();

    partial void OnIsRegexChanged(bool value) => OnPatternChanged();

    partial void OnMatchCaseChanged(bool value) => OnPatternChanged();

    partial void OnFollowChanged(bool value)
    {
        if (value)
            Current = -1;
    }

    /// <summary>A new query: the old one's marks on the logs no longer apply, and the open log is searched again.</summary>
    private void OnPatternChanged()
    {
        if (_searching is { } searching)
        {
            searching.Cancel();
            _searching = null;
            IsSearching = false;
        }
        ClearHits();
        _pattern = null;
        QueryError = null;
        if (Query.Length > 0)
        {
            try
            {
                _pattern = LogSearch.Pattern(Query, IsRegex, MatchCase);
            }
            catch (ArgumentException ex)
            {
                QueryError = "Not a regular expression: " + ex.Message;
            }
        }
        SearchAllCommand.NotifyCanExecuteChanged();
        Finding = FindAsync(jump: true);
    }

    private void ClearHits()
    {
        foreach (var log in Logs)
            log.Hits = null;
        _searchResult = null;
        OnPropertyChanged(nameof(SearchStatus));
    }

    /// <summary>Finds the matches in the open log off the UI thread; with jump, the view goes to the first.</summary>
    private async Task FindAsync(bool jump)
    {
        _finding?.Cancel();
        if (_pattern is not { } pattern)
        {
            Matches = [];
            Current = -1;
            return;
        }
        var cancellation = _finding = new CancellationTokenSource();
        var text = _text;
        try
        {
            var matches = await Task.Run(() => LogSearch.Matches(text, pattern, 0, MaxMatches, cancellation.Token), cancellation.Token);
            if (cancellation.IsCancellationRequested)
                return;
            // The app wrote more meanwhile: its last line and what came after are searched too.
            if (_text.Length > text.Length)
            {
                var lineStart = text.Length == 0 ? 0 : text.LastIndexOf('\n', text.Length - 1) + 1;
                matches.RemoveAll(match => match.Start >= lineStart);
                matches.AddRange(LogSearch.Matches(_text, pattern, lineStart, MaxMatches - matches.Count));
            }
            Matches = matches;
            Current = jump && matches.Count > 0 ? 0 : -1;
            if (Current >= 0)
                Follow = false;
        }
        catch (OperationCanceledException)
        {
            // Another query or log took over.
        }
        catch (RegexMatchTimeoutException)
        {
            Matches = [];
            Current = -1;
            QueryError = "The pattern takes too long to search for. Try a simpler one.";
        }
    }

    // ===== Listing and reading the logs =====

    /// <summary>
    /// Reads nlog.config and lists its logs, off the UI thread. The open log stays open; when it is the live log and a
    /// new one has taken over, such as the next day's, the new one opens.
    /// </summary>
    private async Task ListAsync()
    {
        IReadOnlyList<NLogFileTarget> targets;
        IReadOnlyList<AppLog> logs;
        try
        {
            (targets, logs) = await Task.Run(() =>
            {
                var found = NLogConfig.Read(ConfigPath);
                return (found, AppLogs.Find(found, _zips));
            });
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            Problem = $"{Path.GetFileName(ConfigPath)} can't be read: {ex.Message}";
            ShowLogs([]);
            ClearOpen();
            return;
        }

        Problem = targets.Count == 0 ? $"{Path.GetFileName(ConfigPath)} doesn't write logs to files, so there are none to show here."
            : logs.Count == 0 ? "No logs yet. NLog writes them to " + string.Join(" and ", targets.Select(target => Relative(target.FilePattern))) + "."
            : null;
        var open = Open;
        ShowLogs(logs);

        // The log to show: the same one, unless the live log has moved on; the first live one to start with.
        var next = Logs.FirstOrDefault(log => log.Log.Key == open?.Log.Key);
        if (open?.IsLive == true && next?.IsLive != true)
            next = Logs.FirstOrDefault(log => log.IsLive && log.Log.Target == open.Log.Target) ?? next;
        next ??= Logs.FirstOrDefault();
        if (next is null)
            ClearOpen();
        else if (next.Log.Key != _openKey)
            Show(next);
        else
        {
            Open = next;
            next.IsOpen = true;
        }
    }

    /// <summary>Shows the logs listed. When the same logs are there as before, the rows stay and are brought up to date.</summary>
    private void ShowLogs(IReadOnlyList<AppLog> logs)
    {
        var rows = Logs.ToList();
        if (rows.Select(row => row.Log.Key).SequenceEqual(logs.Select(log => log.Key)) && rows.Count > 0)
        {
            for (var i = 0; i < logs.Count; i++)
                rows[i].Update(logs[i]);
            return;
        }

        var hits = rows.Where(row => row.Hits is not null).ToDictionary(row => row.Log.Key, row => row.Hits);
        var shown = new List<object>();
        foreach (var group in logs.GroupBy(log => log.IsLive).OrderByDescending(group => group.Key))
        {
            shown.Add(new LogSectionViewModel(group.Key ? "LIVE" : "ARCHIVED"));
            shown.AddRange(group.Select(log => new AppLogViewModel(log) { Hits = hits.GetValueOrDefault(log.Key) }));
        }
        Rows = shown;
        SearchAllCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Opens a log: reads it off the UI thread, then follows it if the app is still writing it.</summary>
    private void Show(AppLogViewModel log)
    {
        if (Open is not null)
            Open.IsOpen = false;
        Open = log;
        log.IsOpen = true;
        _openKey = log.Log.Key;
        Follow = true;
        Loading = LoadAsync(log.Log);
    }

    private void ClearOpen()
    {
        _loading?.Cancel();
        _tail = null;
        _openKey = null;
        Open = null;
        Note = null;
        SetText("");
    }

    private async Task LoadAsync(AppLog log)
    {
        _loading?.Cancel();
        var cancellation = _loading = new CancellationTokenSource();
        _tail = null;
        IsLoading = true;
        try
        {
            var read = await Task.Run(() => LogReader.Read(log, LogReader.MaxBytes, cancellation.Token), cancellation.Token);
            if (cancellation.IsCancellationRequested)
                return;
            _tail = read.Tail;
            Note = read.IsCut ? $"Showing the last {Words.Size(read.Text.Length)} of {Words.Size(read.Size)}. Search all reads the whole log." : null;
            SetText(read.Text);
        }
        catch (OperationCanceledException)
        {
            // Another log was opened.
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Note = $"{log.Name} can't be read: {ex.Message}";
            SetText("");
        }
        finally
        {
            if (_loading == cancellation)
                IsLoading = false;
        }
    }

    private void SetText(string text)
    {
        _text = text;
        OnPropertyChanged(nameof(Text));
        Finding = FindAsync(jump: !Follow || Open?.IsLive != true);
    }

    // ===== Following the live log =====

    private void Tick()
    {
        if (++_ticks % ListEvery == 0 && Listing.IsCompleted)
            Listing = ListAsync();
        if (!_following && Loading.IsCompleted)
            _ = FollowAsync();
    }

    /// <summary>Reads what the app has added to the open log since the last look, and adds it to the text.</summary>
    internal async Task FollowAsync()
    {
        if (_tail is not { } tail)
            return;
        _following = true;
        try
        {
            var added = await Task.Run(() => tail.ReadNew());
            // Another log was opened meanwhile.
            if (tail != _tail)
                return;
            if (added is null)
            {
                // NLog has moved the log to an archive and started a new one, or it went: list the logs again.
                Refresh();
                return;
            }
            if (added.Length > 0)
                Append(added);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The app has it locked for a moment; the next tick tries again.
        }
        finally
        {
            _following = false;
        }
    }

    private void Append(string added)
    {
        // A log followed for hours would grow without end; past twice what is shown, its end is read afresh.
        if (_text.Length + added.Length > 2 * LogReader.MaxBytes)
        {
            Refresh();
            return;
        }
        var length = _text.Length;
        _text += added;
        Appended?.Invoke(this, added);
        if (_pattern is not { } pattern)
            return;
        // The last line may have been only part written before, so it is searched again with what was added.
        var lineStart = length == 0 ? 0 : _text.LastIndexOf('\n', length - 1) + 1;
        var kept = Matches.TakeWhile(match => match.Start < lineStart).ToList();
        try
        {
            Matches = [.. kept, .. LogSearch.Matches(_text, pattern, lineStart, MaxMatches - kept.Count)];
        }
        catch (RegexMatchTimeoutException)
        {
            // The matches already found stay; the next search says what went wrong.
        }
    }

    private async Task SearchAllAsync()
    {
        if (_pattern is not { } pattern)
            return;
        _searching?.Cancel();
        var cancellation = _searching = new CancellationTokenSource();
        var logs = Logs.ToList();
        var byKey = logs.ToDictionary(log => log.Log.Key);
        ClearHits();
        Searched = 0;
        SearchTotal = logs.Count;
        SearchProgress = 0;
        IsSearching = true;
        // Progress comes back on the UI thread, a log at a time, so the marks show as the search goes.
        var progress = new Progress<LogSearchProgress>(report =>
        {
            if (cancellation != _searching || !IsSearching)
                return;
            SearchProgress = report.Fraction * 100;
            if (report.Searched is { } searched && byKey.TryGetValue(searched.Key, out var row))
            {
                row.Hits = report.Matches;
                Searched++;
            }
        });
        try
        {
            var counts = await LogSearch.SearchAsync(logs.Select(log => log.Log).ToList(), pattern, progress, cancellation.Token);
            foreach (var log in logs)
                log.Hits = counts.GetValueOrDefault(log.Log.Key);
            var found = logs.Count(log => log.HasHits);
            _searchResult = found == 0 ? $"Not found in any of the {Words.Count(logs.Count, "log", "logs")}"
                : $"Found in {found:N0} of {Words.Count(logs.Count, "log", "logs")}";
        }
        catch (OperationCanceledException)
        {
            _searchResult = cancellation == _searching ? $"Stopped after {Words.Count(Searched, "log", "logs")}" : null;
        }
        catch (RegexMatchTimeoutException)
        {
            _searchResult = "Stopped: the pattern takes too long to search for. Try a simpler one.";
        }
        finally
        {
            if (cancellation == _searching)
            {
                _searching = null;
                IsSearching = false;
                SearchProgress = 0;
            }
            cancellation.Dispose();
        }
    }

    private string Relative(string path)
    {
        var relative = Path.GetRelativePath(Folder, path);
        return relative.StartsWith("..", StringComparison.Ordinal) ? path : relative;
    }

    public void Dispose()
    {
        _timer.Stop();
        _loading?.Cancel();
        _finding?.Cancel();
        _searching?.Cancel();
        _tail = null;
    }
}
