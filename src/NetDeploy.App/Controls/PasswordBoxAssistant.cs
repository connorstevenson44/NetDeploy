using System.Windows;
using System.Windows.Controls;

namespace NetDeploy.App.Controls;

/// <summary>PasswordBox.Password can't be bound, so this attached property mirrors it.</summary>
public static class PasswordBoxAssistant
{
    public static readonly DependencyProperty BoundPasswordProperty = DependencyProperty.RegisterAttached(
        "BoundPassword",
        typeof(string),
        typeof(PasswordBoxAssistant),
        // A null default makes the first bound value (never null) count as a change, which hooks up the event.
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnBoundPasswordChanged));

    public static string GetBoundPassword(DependencyObject element) => (string)element.GetValue(BoundPasswordProperty);

    public static void SetBoundPassword(DependencyObject element, string value) => element.SetValue(BoundPasswordProperty, value);

    private static void OnBoundPasswordChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not PasswordBox box)
            return;

        box.PasswordChanged -= OnPasswordChanged;
        var value = (string?)e.NewValue ?? "";
        if (box.Password != value)
            box.Password = value;
        box.PasswordChanged += OnPasswordChanged;
    }

    private static void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        var box = (PasswordBox)sender;
        SetBoundPassword(box, box.Password);
    }
}
