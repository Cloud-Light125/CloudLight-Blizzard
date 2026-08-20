using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace CloudLightBlizzard.Behaviors;

public static class NestedScrollBehavior
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(NestedScrollBehavior),
        new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element) return;
        if ((bool)e.NewValue) element.PreviewMouseWheel += OnPreviewMouseWheel;
        else element.PreviewMouseWheel -= OnPreviewMouseWheel;
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source) return;

        var comboBox = FindAncestor<ComboBox>(source);
        if (comboBox is not null && !comboBox.IsDropDownOpen)
        {
            e.Handled = true;
            ScrollParent(FindParentScrollViewer(comboBox), e.Delta);
            return;
        }

        var child = FindAncestor<ScrollViewer>(source);
        if (child is null) return;
        var parent = FindParentScrollViewer(child);
        if (parent is null) return;

        var passToParent = child.ScrollableHeight <= 0 ||
                           (e.Delta > 0 && child.VerticalOffset <= 0) ||
                           (e.Delta < 0 && child.VerticalOffset >= child.ScrollableHeight - 0.5);
        if (!passToParent) return;

        e.Handled = true;
        ScrollParent(parent, e.Delta);
    }

    private static void ScrollParent(ScrollViewer? parent, int delta)
    {
        if (parent is null) return;
        var wheelNotches = Math.Max(1, Math.Abs(delta) / 120);
        var configuredLines = SystemParameters.WheelScrollLines;
        var lineCount = configuredLines < 1 || configuredLines > 20 ? 3 : configuredLines;
        for (var i = 0; i < wheelNotches * lineCount; i++)
        {
            if (delta > 0) parent.LineUp();
            else parent.LineDown();
        }
    }

    private static ScrollViewer? FindParentScrollViewer(DependencyObject child)
    {
        var current = GetParent(child);
        while (current is not null)
        {
            if (current is ScrollViewer viewer) return viewer;
            current = GetParent(current);
        }
        return null;
    }

    private static T? FindAncestor<T>(DependencyObject child) where T : DependencyObject
    {
        var current = child;
        while (current is not null)
        {
            if (current is T match) return match;
            current = GetParent(current);
        }
        return null;
    }

    private static DependencyObject? GetParent(DependencyObject child)
    {
        if (child is Visual or System.Windows.Media.Media3D.Visual3D)
        {
            var visualParent = VisualTreeHelper.GetParent(child);
            if (visualParent is not null) return visualParent;
        }
        return LogicalTreeHelper.GetParent(child);
    }
}
