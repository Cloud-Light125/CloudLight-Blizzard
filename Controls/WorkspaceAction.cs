using System.Windows;
using System.Windows.Controls.Primitives;

namespace CloudLightBlizzard.Controls;

public sealed class WorkspaceActionEventArgs(string action, object source) : RoutedEventArgs(WorkspaceAction.RequestEvent, source)
{
    public string Action { get; } = action;
    public object Control { get; } = source;
}

/// <summary>Typed owner boundary: reusable visual components request existing page actions.</summary>
public static class WorkspaceAction
{
    public static readonly RoutedEvent RequestEvent = EventManager.RegisterRoutedEvent("Request", RoutingStrategy.Bubble, typeof(EventHandler<WorkspaceActionEventArgs>), typeof(WorkspaceAction));
    public static readonly DependencyProperty NameProperty = DependencyProperty.RegisterAttached("Name", typeof(string), typeof(WorkspaceAction), new PropertyMetadata("", Changed));
    public static string GetName(DependencyObject d) => (string)d.GetValue(NameProperty);
    public static void SetName(DependencyObject d, string value) => d.SetValue(NameProperty, value);
    private static void Changed(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ButtonBase button) { button.Click -= Clicked; if (!string.IsNullOrWhiteSpace(e.NewValue as string)) button.Click += Clicked; }
    }
    private static void Clicked(object sender, RoutedEventArgs e)
    {
        if (sender is UIElement element) { element.RaiseEvent(new WorkspaceActionEventArgs(GetName(element), sender)); e.Handled = true; }
    }
}
