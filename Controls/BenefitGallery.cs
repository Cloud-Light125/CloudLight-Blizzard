using System.Windows;
using System.Windows.Controls;
namespace CloudLightBlizzard.Controls;

/// <summary>All benefits in Core order. No First/FirstOrDefault image selection.</summary>
public sealed class BenefitGallery : ItemsControl
{
    public static readonly DependencyProperty IsCompactProperty = DependencyProperty.Register(nameof(IsCompact), typeof(bool), typeof(BenefitGallery), new PropertyMetadata(false));
    public bool IsCompact { get => (bool)GetValue(IsCompactProperty); set => SetValue(IsCompactProperty, value); }
    public BenefitGallery() => SetResourceReference(StyleProperty, "BenefitGalleryStyle");
}
