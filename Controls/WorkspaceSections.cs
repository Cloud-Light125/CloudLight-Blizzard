using System.Windows;
using System.Windows.Controls;

namespace CloudLightBlizzard.Controls;

/// <summary>Switch cached sections without recreating controls or discarding unsaved input.</summary>
internal static class WorkspaceSections
{
    internal static void Show(DependencyObject root, string section)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is FrameworkElement { Tag: string tag } element && tag.StartsWith("workspace:", StringComparison.Ordinal))
                element.Visibility = tag[10..] == section ? Visibility.Visible : Visibility.Collapsed;
            else if (child is not ItemsControl)
                Show(child, section);
        }
    }
}
