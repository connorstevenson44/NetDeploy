using MaterialDesignThemes.Wpf;

namespace NetDeploy.App.ViewModels;

/// <summary>One line of a dry run's summary.</summary>
public sealed record SummaryLine(PackIconKind Icon, string Text);
