using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using NetDeploy.App.ViewModels;
using NetDeploy.Core.Logs;

namespace NetDeploy.App;

/// <summary>
/// An app's log, read only: lines coloured by their level, the search's matches highlighted with the one in view
/// stronger, and new lines added at the end as the app writes them. Scrolling to the end follows the log; scrolling
/// up stops following it. Only the lines on screen are drawn, so a log of millions of lines scrolls smoothly.
/// </summary>
public sealed partial class LogViewer : TextEditor
{
    public static readonly DependencyProperty LogsProperty = DependencyProperty.Register(nameof(Logs), typeof(LogsViewModel), typeof(LogViewer),
        new PropertyMetadata(null, (d, e) => ((LogViewer)d).OnLogsChanged((LogsViewModel?)e.OldValue, (LogsViewModel?)e.NewValue)));

    public LogViewer()
    {
        IsReadOnly = true;
        ShowLineNumbers = true;
        FontFamily = new FontFamily("Cascadia Mono, Consolas");
        FontSize = 12;
        Padding = new Thickness(4, 4, 0, 4);
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        Options.EnableHyperlinks = false;
        Options.EnableEmailHyperlinks = false;
        Document.UndoStack.SizeLimit = 0;
        TextArea.TextView.LineTransformers.Add(new LevelColorizer(this));
        TextArea.TextView.BackgroundRenderers.Add(new MatchRenderer(this));
        // Only the person's own scrolling changes whether the log is followed; the text growing doesn't.
        AddHandler(ScrollBar.ScrollEvent, new ScrollEventHandler((_, _) => AfterScrolling()));
        PreviewMouseWheel += (_, _) => AfterScrolling();
        PreviewKeyDown += (_, _) => AfterScrolling();
        AutomationProperties.SetAutomationId(this, "LogText");
    }

    /// <summary>The logs whose open log is shown.</summary>
    public LogsViewModel? Logs
    {
        get => (LogsViewModel?)GetValue(LogsProperty);
        set => SetValue(LogsProperty, value);
    }

    internal Brush Resource(string key) => (Brush)FindResource(key);

    private void OnLogsChanged(LogsViewModel? old, LogsViewModel? logs)
    {
        if (old is not null)
        {
            old.PropertyChanged -= OnLogsPropertyChanged;
            old.Appended -= OnAppended;
        }
        if (logs is not null)
        {
            logs.PropertyChanged += OnLogsPropertyChanged;
            logs.Appended += OnAppended;
        }
        ShowText();
    }

    private void OnLogsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(LogsViewModel.Text):
                ShowText();
                break;
            case nameof(LogsViewModel.Matches):
                TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
                break;
            case nameof(LogsViewModel.Current):
                GoToCurrent();
                break;
            case nameof(LogsViewModel.Follow) when Logs?.Follow == true:
                ScrollToEnd();
                break;
        }
    }

    private void ShowText()
    {
        Document.Text = Logs?.Text ?? "";
        if (Logs?.Follow == true)
            ScrollToEnd();
        else
            GoToCurrent();
    }

    private void OnAppended(object? sender, string added)
    {
        Document.Insert(Document.TextLength, added);
        if (Logs?.Follow == true)
            ScrollToEnd();
    }

    private void GoToCurrent()
    {
        TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
        if (Logs?.CurrentMatch is not { } match || match.Start + match.Length > Document.TextLength)
            return;
        var location = Document.GetLocation(match.Start);
        CaretOffset = match.Start;
        ScrollTo(location.Line, location.Column);
    }

    /// <summary>Scrolling up stops following the log; scrolling back to its end follows it again.</summary>
    private void AfterScrolling() => Dispatcher.BeginInvoke(() =>
    {
        if (Logs is not { } logs)
            return;
        var view = TextArea.TextView;
        var atEnd = view.VerticalOffset + view.ActualHeight >= view.DocumentHeight - view.DefaultLineHeight;
        if (logs.Follow != atEnd)
            logs.Follow = atEnd;
    }, System.Windows.Threading.DispatcherPriority.Input);

    /// <summary>Colours a line by its level, read near its start so a word in the message doesn't count.</summary>
    private sealed partial class LevelColorizer(LogViewer viewer) : DocumentColorizingTransformer
    {
        private const int LevelWithin = 120;

        [GeneratedRegex(@"\b(FATAL|Fatal|CRITICAL|Critical|CRIT|FTL|ERROR|Error|ERR|EROR)\b")]
        private static partial Regex Error();

        [GeneratedRegex(@"\b(WARN|Warn|WARNING|Warning|WRN)\b")]
        private static partial Regex Warning();

        [GeneratedRegex(@"\b(DEBUG|Debug|DBG|TRACE|Trace|TRC|VERBOSE|Verbose)\b")]
        private static partial Regex Quiet();

        protected override void ColorizeLine(DocumentLine line)
        {
            if (line.Length == 0)
                return;
            var start = CurrentContext.Document.GetText(line.Offset, Math.Min(line.Length, LevelWithin));
            var brush = Error().IsMatch(start) ? viewer.Resource("Danger")
                : Warning().IsMatch(start) ? viewer.Resource("Warning")
                : Quiet().IsMatch(start) ? viewer.Resource("InkMuted")
                : null;
            if (brush is not null)
                ChangeLinePart(line.Offset, line.EndOffset, element => element.TextRunProperties.SetForegroundBrush(brush));
        }
    }

    /// <summary>Draws the matches on screen behind the text, the current one stronger.</summary>
    private sealed class MatchRenderer(LogViewer viewer) : IBackgroundRenderer
    {
        public KnownLayer Layer => KnownLayer.Selection;

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            if (viewer.Logs is not { } logs || logs.Matches.Count == 0 || textView.VisualLines.Count == 0)
                return;
            var matches = logs.Matches;
            var first = textView.VisualLines[0].FirstDocumentLine.Offset;
            var last = textView.VisualLines[^1].LastDocumentLine.EndOffset;
            var current = logs.CurrentMatch;
            var hit = viewer.Resource("SearchHit");
            var strong = viewer.Resource("SearchHitCurrent");
            for (var i = FirstAtOrAfter(matches, first); i < matches.Count && matches[i].Start <= last; i++)
            {
                var match = matches[i];
                if (match.Start + match.Length > viewer.Document.TextLength)
                    break;
                var builder = new BackgroundGeometryBuilder { CornerRadius = 2, AlignToWholePixels = true };
                builder.AddSegment(textView, new TextSegment { StartOffset = match.Start, Length = match.Length });
                if (builder.CreateGeometry() is { } geometry)
                    drawingContext.DrawGeometry(match == current ? strong : hit, null, geometry);
            }
        }

        /// <summary>The first match that starts at or after offset; matches are in order.</summary>
        private static int FirstAtOrAfter(IReadOnlyList<LogMatch> matches, int offset)
        {
            int low = 0, high = matches.Count;
            while (low < high)
            {
                var middle = (low + high) / 2;
                if (matches[middle].Start < offset)
                    low = middle + 1;
                else
                    high = middle;
            }
            return low;
        }
    }
}
