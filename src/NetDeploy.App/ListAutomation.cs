using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace NetDeploy.App;

/// <summary>
/// Keeps a list's rows readable by screen readers and UI tests after its items are replaced. WPF reuses the
/// automation peer of an item that is in both the old and the new items, but keeps the children it read from the
/// item's old row, so the row's text and buttons drop out of UI Automation. Folding a folder in a file tree does that.
/// </summary>
public static class ListAutomation
{
    public static readonly DependencyProperty RefreshRowsProperty = DependencyProperty.RegisterAttached(
        "RefreshRows", typeof(bool), typeof(ListAutomation), new PropertyMetadata(false, OnRefreshRowsChanged));

    public static bool GetRefreshRows(ItemsControl list) => (bool)list.GetValue(RefreshRowsProperty);

    public static void SetRefreshRows(ItemsControl list, bool value) => list.SetValue(RefreshRowsProperty, value);

    private static readonly DependencyProperty HandlerProperty = DependencyProperty.RegisterAttached(
        "Handler", typeof(ItemsChangedEventHandler), typeof(ListAutomation));

    private static void OnRefreshRowsChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not ItemsControl list)
            return;
        if (list.GetValue(HandlerProperty) is ItemsChangedEventHandler old)
            list.ItemContainerGenerator.ItemsChanged -= old;
        if (!(bool)e.NewValue)
            return;
        ItemsChangedEventHandler handler = (_, _) => RefreshRows(list);
        list.SetValue(HandlerProperty, handler);
        list.ItemContainerGenerator.ItemsChanged += handler;
    }

    // Once the new rows are laid out, the list and each row's peer read their children again. The peer only exists
    // while something reads UI Automation, so this costs nothing otherwise.
    private static void RefreshRows(ItemsControl list) =>
        list.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (UIElementAutomationPeer.FromElement(list) is not { } peer)
                return;
            peer.ResetChildrenCache();
            foreach (var row in peer.GetChildren() ?? [])
                row.ResetChildrenCache();
        });
}
