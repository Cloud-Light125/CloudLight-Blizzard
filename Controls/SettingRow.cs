using System.Windows;
using System.Windows.Controls;
namespace CloudLightBlizzard.Controls;

public sealed class SettingRow : ContentControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(nameof(Title), typeof(string), typeof(SettingRow), new PropertyMetadata(""));
    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(nameof(Description), typeof(string), typeof(SettingRow), new PropertyMetadata(""));
    public static readonly DependencyProperty EditorProperty = DependencyProperty.Register(nameof(Editor), typeof(object), typeof(SettingRow));
    public static readonly DependencyProperty ReflowWidthProperty = DependencyProperty.Register(nameof(ReflowWidth), typeof(double), typeof(SettingRow), new PropertyMetadata(520d));
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Description { get => (string)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public object Editor { get => GetValue(EditorProperty); set => SetValue(EditorProperty, value); }
    public double ReflowWidth { get => (double)GetValue(ReflowWidthProperty); set => SetValue(ReflowWidthProperty, value); }
    public SettingRow() => SetResourceReference(StyleProperty, "SettingRowControl");
}
