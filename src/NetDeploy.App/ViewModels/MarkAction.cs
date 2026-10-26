using System.Windows.Input;
using MaterialDesignThemes.Wpf;

namespace NetDeploy.App.ViewModels;

/// <summary>A button on a mark, such as putting the server's text back.</summary>
public sealed record MarkAction(string Name, PackIconKind Icon, string Tip, ICommand Command);
