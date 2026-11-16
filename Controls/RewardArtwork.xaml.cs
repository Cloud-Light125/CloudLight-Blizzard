using System.Windows;
using System.Windows.Controls;
namespace CloudLightBlizzard.Controls;

public partial class RewardArtwork : UserControl
{
    public static readonly DependencyProperty IsCompactProperty = DependencyProperty.Register(nameof(IsCompact), typeof(bool), typeof(RewardArtwork), new PropertyMetadata(false));
    public bool IsCompact { get => (bool)GetValue(IsCompactProperty); set => SetValue(IsCompactProperty, value); }
    public RewardArtwork() => InitializeComponent();
}
