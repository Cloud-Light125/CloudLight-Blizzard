using System.Windows;
using System.Windows.Controls;
using CloudLightBlizzard.ViewModels.Visuals;

namespace CloudLightBlizzard.Controls;

/// <summary>Shared presentation for official progress. Actions are routed through the existing workspace.</summary>
public partial class RewardCard : UserControl
{
    public static readonly DependencyProperty CompactProperty=DependencyProperty.Register(nameof(Compact),typeof(bool),typeof(RewardCard));
    public bool Compact {get=>(bool)GetValue(CompactProperty);set=>SetValue(CompactProperty,value);}
    public static readonly DependencyProperty RewardProperty=DependencyProperty.Register(nameof(Reward),typeof(RewardVisual),typeof(RewardCard));
    public static readonly DependencyProperty TitleProperty=DependencyProperty.Register(nameof(Title),typeof(string),typeof(RewardCard),new PropertyMetadata(""));
    public static readonly DependencyProperty ImageUrlProperty=DependencyProperty.Register(nameof(ImageUrl),typeof(string),typeof(RewardCard),new PropertyMetadata(""));
    public static readonly DependencyProperty SubtitleProperty=DependencyProperty.Register(nameof(Subtitle),typeof(string),typeof(RewardCard),new PropertyMetadata(""));
    public static readonly DependencyProperty DetailsVisibilityProperty=DependencyProperty.Register(nameof(DetailsVisibility),typeof(Visibility),typeof(RewardCard),new PropertyMetadata(Visibility.Visible));
    public Visibility DetailsVisibility {get=>(Visibility)GetValue(DetailsVisibilityProperty);set=>SetValue(DetailsVisibilityProperty,value);}
    public RewardVisual? Reward {get=>(RewardVisual?)GetValue(RewardProperty);set=>SetValue(RewardProperty,value);}
    public string Title {get=>(string)GetValue(TitleProperty);set=>SetValue(TitleProperty,value);}
    public string ImageUrl {get=>(string)GetValue(ImageUrlProperty);set=>SetValue(ImageUrlProperty,value);}
    public string Subtitle {get=>(string)GetValue(SubtitleProperty);set=>SetValue(SubtitleProperty,value);}
    public RewardCard()=>InitializeComponent();
}
