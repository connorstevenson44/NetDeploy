using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Rendering;
using MaterialDesignThemes.Wpf;
using NetDeploy.App.ViewModels;

namespace NetDeploy.App;

/// <summary>
/// A text editor that tracks changes like a word processor: text that is new shows on green, text taken out shows
/// struck through in red where it was, and whole lines taken out show struck through above the line that followed
/// them. Each change has small buttons at the end of its line to undo it or take the package's text instead. The text
/// and the marks come from a ConfigFileViewModel, which works the marks out again after every edit.
/// </summary>
public sealed class ChangeEditor : TextEditor
{
    public static readonly DependencyProperty BoundTextProperty = DependencyProperty.Register(nameof(BoundText), typeof(string), typeof(ChangeEditor),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, e) => ((ChangeEditor)d).ShowText((string?)e.NewValue ?? "")));

    public static readonly DependencyProperty MarksProperty = DependencyProperty.Register(nameof(Marks), typeof(IReadOnlyList<ChangeMark>), typeof(ChangeEditor),
        new PropertyMetadata(Array.Empty<ChangeMark>(), (d, _) => ((ChangeEditor)d).TextArea.TextView.Redraw()));

    public static readonly DependencyProperty FileNameProperty = DependencyProperty.Register(nameof(FileName), typeof(string), typeof(ChangeEditor),
        new PropertyMetadata("", (d, e) => ((ChangeEditor)d).SyntaxHighlighting = Highlighting((string?)e.NewValue ?? "")));

    private bool _showing;
    private ChangeMark? _hovered;

    public ChangeEditor()
    {
        ShowLineNumbers = true;
        FontFamily = new FontFamily("Cascadia Mono, Consolas");
        FontSize = 12.5;
        Padding = new Thickness(4, 4, 0, 4);
        // Long lines wrap, so a change and the buttons at the end of its line are always in view.
        WordWrap = true;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        Options.EnableHyperlinks = false;
        Options.EnableEmailHyperlinks = false;
        Options.IndentationSize = 2;
        TextArea.TextView.BackgroundRenderers.Add(new MarkRenderer(this));
        TextArea.TextView.ElementGenerators.Add(new MarkGenerator(this));
        AutomationProperties.SetAutomationId(this, "FileText");
        // The buttons on the changes come and go as lines are drawn, so UI Automation has to ask for them again.
        TextArea.TextView.VisualLinesChanged += (_, _) => UIElementAutomationPeer.FromElement(this)?.ResetChildrenCache();
        TextChanged += (_, _) =>
        {
            if (!_showing)
                SetCurrentValue(BoundTextProperty, Text);
        };
    }

    /// <summary>The text, bound to the view model; the editor's own Text can't be bound.</summary>
    public string BoundText
    {
        get => (string)GetValue(BoundTextProperty);
        set => SetValue(BoundTextProperty, value);
    }

    public IReadOnlyList<ChangeMark> Marks
    {
        get => (IReadOnlyList<ChangeMark>)GetValue(MarksProperty);
        set => SetValue(MarksProperty, value);
    }

    /// <summary>The file's name, which picks the colouring of its syntax.</summary>
    public string FileName
    {
        get => (string)GetValue(FileNameProperty);
        set => SetValue(FileNameProperty, value);
    }

    /// <summary>The mark whose buttons the mouse is over, which shows stronger so it is clear what they act on.</summary>
    internal ChangeMark? Hovered
    {
        get => _hovered;
        set
        {
            _hovered = value;
            TextArea.TextView.InvalidateLayer(KnownLayer.Background);
        }
    }

    /// <summary>Moves to the next mark after the caret, or with -1, the one before it.</summary>
    public void GoToMark(int direction)
    {
        var caret = CaretOffset;
        var marks = Marks.Where(mark => mark.End <= Document.TextLength).ToList();
        var next = direction > 0
            ? marks.FirstOrDefault(mark => mark.Start > caret) ?? marks.FirstOrDefault()
            : marks.LastOrDefault(mark => mark.Start < caret) ?? marks.LastOrDefault();
        if (next is null)
            return;
        CaretOffset = next.Start;
        var line = Document.GetLineByOffset(next.Start);
        ScrollTo(line.LineNumber, 0);
        TextArea.Focus();
    }

    /// <summary>
    /// Shows text from the view model. Only the part that differs is replaced, so the caret and the scroll stay
    /// where they are and the change can be undone like typing.
    /// </summary>
    private void ShowText(string text)
    {
        if (text == Text)
            return;
        _showing = true;
        try
        {
            var old = Document.Text;
            var start = 0;
            while (start < old.Length && start < text.Length && old[start] == text[start])
                start++;
            var end = 0;
            while (end < old.Length - start && end < text.Length - start && old[^(end + 1)] == text[^(end + 1)])
                end++;
            Document.Replace(start, old.Length - start - end, text.Substring(start, text.Length - start - end));
        }
        finally
        {
            _showing = false;
        }
    }

    private static IHighlightingDefinition? Highlighting(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".json" => HighlightingManager.Instance.GetDefinition("JavaScript"),
        var extension => HighlightingManager.Instance.GetDefinitionByExtension(extension),
    };

    internal Brush Resource(string key) => (Brush)FindResource(key);

    protected override AutomationPeer OnCreateAutomationPeer() => new EditorPeer(this);

    /// <summary>
    /// The editor to UI Automation and screen readers: its text, which can be read and set, and the buttons on the
    /// changes in view, which AvalonEdit's own peer leaves out.
    /// </summary>
    private sealed class EditorPeer(ChangeEditor editor) : FrameworkElementAutomationPeer(editor), IValueProvider
    {
        public bool IsReadOnly => editor.IsReadOnly;

        public string Value => editor.Text;

        public void SetValue(string value)
        {
            if (IsReadOnly)
                throw new ElementNotEnabledException();
            editor.Text = value;
        }

        public override object GetPattern(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.Value ? this : base.GetPattern(patternInterface);

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Document;

        protected override string GetClassNameCore() => nameof(ChangeEditor);

        protected override List<AutomationPeer> GetChildrenCore() =>
            [.. base.GetChildrenCore() ?? [], .. Buttons(editor.TextArea.TextView).Select(CreatePeerForElement)];

        private static IEnumerable<Button> Buttons(DependencyObject parent)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is Button button)
                    yield return button;
                else
                    foreach (var inner in Buttons(child))
                        yield return inner;
            }
        }
    }

    /// <summary>Paints the marked text: green for new text, yellow for a setting that needs a value.</summary>
    private sealed class MarkRenderer(ChangeEditor editor) : IBackgroundRenderer
    {
        public KnownLayer Layer => KnownLayer.Background;

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            var length = editor.Document.TextLength;
            foreach (var mark in editor.Marks)
            {
                if (mark.Length == 0 || mark.End > length)
                    continue;
                var hovered = ReferenceEquals(mark, editor.Hovered);
                var (fill, border) = mark.Kind switch
                {
                    MarkKind.NeedsChoice => (editor.Resource("DiffNeedsValue"), editor.Resource("Warning")),
                    MarkKind.PackageDiffers => (hovered ? editor.Resource("BrandTint") : Brushes.Transparent, editor.Resource("Brand")),
                    _ => (editor.Resource(hovered ? "DiffAddedGutter" : "DiffAdded"), editor.Resource("Success")),
                };
                // New lines are marked across the whole width, like a diff; new words just where they are.
                var wholeLines = mark.Start == editor.Document.GetLineByOffset(mark.Start).Offset && editor.Document.GetText(mark.Start, mark.Length).EndsWith('\n');
                var builder = new BackgroundGeometryBuilder { CornerRadius = wholeLines ? 0 : 2, AlignToWholePixels = true, ExtendToFullWidthAtLineEnd = wholeLines };
                builder.AddSegment(textView, new TextSegment { StartOffset = mark.Start, Length = wholeLines ? mark.Length - 1 : mark.Length });
                if (builder.CreateGeometry() is { } geometry)
                {
                    var pen = mark.Kind == MarkKind.Change && !hovered ? null : new Pen(border, mark.Kind == MarkKind.PackageDiffers && !hovered ? 0.8 : 1) { DashStyle = mark.Kind == MarkKind.PackageDiffers ? DashStyles.Dash : DashStyles.Solid };
                    drawingContext.DrawGeometry(fill, pen, geometry);
                }
            }
        }
    }

    /// <summary>
    /// Puts what can't be in the text itself among it: the server's text that was taken out, where it was, and the
    /// buttons of each mark at the end of the line it ends on. None of it takes up any of the text.
    /// </summary>
    private sealed class MarkGenerator(ChangeEditor editor) : VisualLineElementGenerator
    {
        public override int GetFirstInterestedOffset(int startOffset)
        {
            var first = -1;
            foreach (var offset in Offsets())
                if (offset >= startOffset && (first < 0 || offset < first))
                    first = offset;
            return first;
        }

        public override VisualLineElement? ConstructElement(int offset)
        {
            var textView = CurrentContext.TextView;
            var removed = editor.Marks.Where(mark => mark.Kind == MarkKind.Change && mark.Removed.Length > 0 && mark.Start == offset).ToList();
            var buttons = editor.IsReadOnly ? [] : editor.Marks.Where(mark => mark.Actions.Count > 0 && ButtonsAt(mark) == offset).ToList();
            if (removed.Count == 0 && buttons.Count == 0)
                return null;

            var lineHeight = textView.DefaultLineHeight;
            var atLineStart = offset == editor.Document.GetLineByOffset(offset).Offset;
            var panel = new StackPanel();
            var row = new StackPanel { Orientation = Orientation.Horizontal, Height = lineHeight };
            foreach (var mark in removed)
            {
                // Whole lines show above the line after them; part of a line shows inline, struck through.
                if (atLineStart && mark.Removed.EndsWith('\n'))
                    panel.Children.Add(new RemovedLines(editor, textView, mark.Removed));
                else
                    row.Children.Add(Struck(mark.Removed));
            }
            foreach (var mark in buttons)
                row.Children.Add(Pill(mark));
            panel.Children.Add(row);
            var above = panel.Children.OfType<RemovedLines>().Sum(lines => lines.Height);
            TextBlock.SetBaselineOffset(panel, above + textView.DefaultBaseline);
            return new InlineObjectElement(0, panel);
        }

        private IEnumerable<int> Offsets()
        {
            var length = editor.Document.TextLength;
            foreach (var mark in editor.Marks)
            {
                if (mark.End > length)
                    continue;
                if (mark.Kind == MarkKind.Change && mark.Removed.Length > 0)
                    yield return mark.Start;
                if (!editor.IsReadOnly && mark.Actions.Count > 0)
                    yield return ButtonsAt(mark);
            }
        }

        // The end of the line the mark ends on, or for text taken out, the line it was taken out of.
        private int ButtonsAt(ChangeMark mark) =>
            editor.Document.GetLineByOffset(mark.Length > 0 ? mark.End - 1 : mark.Start).EndOffset;

        private TextBlock Struck(string text) => new()
        {
            Text = text.Replace("\r\n", "↵").Replace('\n', '↵').Replace('\r', '↵').Replace("\t", "  "),
            FontFamily = editor.FontFamily,
            FontSize = editor.FontSize,
            Foreground = editor.Resource("Danger"),
            Background = editor.Resource("DiffRemoved"),
            TextDecorations = TextDecorations.Strikethrough,
            VerticalAlignment = VerticalAlignment.Center,
        };

        private Border Pill(ChangeMark mark)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            if (mark.Kind == MarkKind.NeedsChoice)
                content.Children.Add(new TextBlock
                {
                    Text = "needs a value",
                    FontSize = 11,
                    Foreground = editor.Resource("Warning"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(6, 0, 2, 0),
                });
            foreach (var action in mark.Actions)
            {
                var button = new Button
                {
                    Style = (Style)editor.FindResource("MarkButton"),
                    Command = action.Command,
                    ToolTip = action.Tip,
                    Content = new PackIcon { Kind = action.Icon, Width = 13, Height = 13 },
                };
                AutomationProperties.SetName(button, action.Name);
                AutomationProperties.SetHelpText(button, action.Tip);
                content.Children.Add(button);
            }
            var pill = new Border
            {
                Child = content,
                Height = CurrentContext.TextView.DefaultLineHeight,
                Margin = new Thickness(8, 0, 0, 0),
                Padding = new Thickness(1, 0, 1, 0),
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1),
                BorderBrush = editor.Resource(mark.Kind == MarkKind.NeedsChoice ? "Warning" : "Hairline"),
                Background = editor.Resource(mark.Kind == MarkKind.NeedsChoice ? "WarningTint" : "Surface"),
                Cursor = System.Windows.Input.Cursors.Arrow,
                ToolTip = mark.Note.Length > 0 ? mark.Note : null,
            };
            pill.MouseEnter += (_, _) => editor.Hovered = mark;
            pill.MouseLeave += (_, _) => editor.Hovered = null;
            return pill;
        }
    }

    /// <summary>
    /// Lines taken out, struck through on red, in rows of their own above the line that followed them. It takes no
    /// width, so the line's own text doesn't move; it draws across the editor from the start of the line.
    /// </summary>
    private sealed class RemovedLines : FrameworkElement
    {
        private readonly ChangeEditor _editor;
        private readonly TextView _textView;
        private readonly string[] _lines;

        public RemovedLines(ChangeEditor editor, TextView textView, string removed)
        {
            _editor = editor;
            _textView = textView;
            _lines = removed.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n');
            Height = _lines.Length * textView.DefaultLineHeight;
            IsHitTestVisible = false;
        }

        protected override Size MeasureOverride(Size availableSize) => new(0, Height);

        protected override void OnRender(DrawingContext drawingContext)
        {
            var lineHeight = _textView.DefaultLineHeight;
            var width = _textView.ActualWidth + _textView.HorizontalOffset;
            var typeface = new Typeface(_editor.FontFamily, _editor.FontStyle, _editor.FontWeight, _editor.FontStretch);
            var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            for (var i = 0; i < _lines.Length; i++)
            {
                drawingContext.DrawRectangle(_editor.Resource("DiffRemoved"), null, new Rect(0, i * lineHeight, width, lineHeight));
                var text = new FormattedText(_lines[i].Replace("\t", new string(' ', _editor.Options.IndentationSize)), CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight, typeface, _editor.FontSize, _editor.Resource("Danger"), dpi);
                text.SetTextDecorations(TextDecorations.Strikethrough);
                drawingContext.DrawText(text, new Point(0, i * lineHeight + (lineHeight - text.Height) / 2));
            }
        }
    }
}
