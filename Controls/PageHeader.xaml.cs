using System.Windows;
using System.Windows.Controls;

namespace CloudLightBlizzard.Controls;

public partial class PageHeader : UserControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(PageHeader), new PropertyMetadata(""));
    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(PageHeader), new PropertyMetadata(""));
    public static readonly DependencyProperty HeaderActionsProperty = DependencyProperty.Register(
        nameof(HeaderActions), typeof(object), typeof(PageHeader), new PropertyMetadata(null));

    public static readonly DependencyProperty IsCompactProperty = DependencyProperty.Register(
        nameof(IsCompact), typeof(bool), typeof(PageHeader), new PropertyMetadata(false, (d, _) => ((PageHeader)d).ApplyCompact()));

    public PageHeader() { InitializeComponent(); ApplyCompact(); }
    public bool IsCompact { get => (bool)GetValue(IsCompactProperty); set => SetValue(IsCompactProperty, value); }
    private void ApplyCompact()
    {
        if (TitleLine is null || DescriptionLine is null) return;
        DescriptionLine.Visibility = IsCompact ? Visibility.Collapsed : Visibility.Visible;
        if (IsCompact) TitleLine.FontSize = 22; else TitleLine.ClearValue(TextBlock.FontSizeProperty);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public object? HeaderActions
    {
        get => GetValue(HeaderActionsProperty);
        set => SetValue(HeaderActionsProperty, value);
    }
}
