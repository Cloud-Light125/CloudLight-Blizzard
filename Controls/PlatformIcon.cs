using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;

namespace CloudLightBlizzard.Controls;

/// <summary>Small vector platform marks, sized in DIPs and independent of account data.</summary>
public sealed class PlatformIcon : Viewbox
{
    public static readonly DependencyProperty PlatformProperty = DependencyProperty.Register(nameof(Platform), typeof(string), typeof(PlatformIcon), new PropertyMetadata("Twitch", Changed));
    public string Platform { get => (string)GetValue(PlatformProperty); set => SetValue(PlatformProperty, value); }
    public PlatformIcon() { Width = 24; Height = 24; Draw(); }
    private static void Changed(DependencyObject value, DependencyPropertyChangedEventArgs e) => ((PlatformIcon)value).Draw();
    private void Draw()
    {
        var canvas = new Canvas { Width = 28, Height = 28 };
        var color = Platform switch { "Twitch" => "#9146FF", "Bilibili" => "#ED69AA", "YouTube" => "#EA3546", _ => "#635BEE" };
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        if (Platform == "Twitch")
        {
            canvas.Children.Add(new Path { Fill = brush, Data = Geometry.Parse("M3,1 H27 V18 L20,25 H14 L9,28 V25 H3 Z M7,5 V20 H12 V23 L16,20 H20 L23,17 V5 Z") });
            canvas.Children.Add(new Path { Stroke = brush, StrokeThickness = 2.6, Data = Geometry.Parse("M13,9 V16 M19,9 V16") });
        }
        else
        {
            var box = new Border { Width = 28, Height = 22, CornerRadius = new CornerRadius(6), Background = brush };Canvas.SetTop(box,3);canvas.Children.Add(box);
            canvas.Children.Add(new Path { Fill = Brushes.White, Data = Geometry.Parse(Platform == "YouTube" ? "M11,9 L20,14 L11,19 Z" : Platform == "Bilibili" ? "M8,10 H10 V16 H8 Z M18,10 H20 V16 H18 Z" : "M5,14 C5,7 12,7 14,14 C16,21 23,21 23,14 C23,7 16,7 14,14 C12,21 5,21 5,14 Z") });
        }
        Child = canvas;
    }
}
