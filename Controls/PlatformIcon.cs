using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;

namespace CloudLightBlizzard.Controls;

/// <summary>Scales platform marks uniformly within a square icon slot.</summary>
public sealed class PlatformIcon : Viewbox
{
    private static readonly Lazy<BitmapFrame> SoopMark = new(LoadSoopMark);

    public static readonly DependencyProperty PlatformProperty = DependencyProperty.Register(
        nameof(Platform), typeof(string), typeof(PlatformIcon),
        new PropertyMetadata("Twitch", (icon, _) => ((PlatformIcon)icon).Draw()));

    public string Platform
    {
        get => (string)GetValue(PlatformProperty);
        set => SetValue(PlatformProperty, value);
    }

    public PlatformIcon()
    {
        Draw();
    }

    private void Draw()
    {
        Child = Platform switch
        {
            "BattleNet" => CreateBadge(CreateMark("GeoBattleNetMark", "#168DEC")),
            "Bilibili" => CreateBadge(CreateMark("GeoBilibiliMark", "#008DB8")),
            "Overwatch" => CreateBadge(CreateOverwatchMark()),
            "Soop" => new Image { Source = SoopMark.Value, Stretch = Stretch.Uniform },
            _ => CreateStreamingMark()
        };
    }

    private static Border CreateBadge(UIElement mark)
    {
        var badge = new Border
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(5),
            Child = mark
        };
        badge.SetResourceReference(Border.BackgroundProperty, "App.SurfaceAlt");
        return badge;
    }

    private static Path CreateMark(string geometryKey, string? color = null)
    {
        var mark = new Path
        {
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        mark.SetResourceReference(Path.DataProperty, geometryKey);
        if (color is null)
            mark.SetResourceReference(Shape.FillProperty, "Text.Primary");
        else
            mark.Fill = BrushFromColor(color);
        return mark;
    }

    private static Viewbox CreateOverwatchMark()
    {
        // Both parts share the SVG's original square coordinate system.
        var artwork = new Canvas { Width = 600, Height = 600 };
        var arc = CreateMark("GeoOverwatchArc", "#FA9C1E");
        var ring = CreateMark("GeoOverwatchRing");
        arc.Stretch = ring.Stretch = Stretch.None;
        artwork.Children.Add(arc);
        artwork.Children.Add(ring);
        return new Viewbox { Child = artwork, Stretch = Stretch.Uniform };
    }

    private Canvas CreateStreamingMark()
    {
        var canvas = new Canvas { Width = 28, Height = 28 };
        if (Platform == "Twitch")
        {
            var purple = BrushFromColor("#9146FF");
            canvas.Children.Add(new Path
            {
                Fill = purple,
                Data = Geometry.Parse("M3,1 H27 V18 L20,25 H14 L9,28 V25 H3 Z M7,5 V20 H12 V23 L16,20 H20 L23,17 V5 Z")
            });
            canvas.Children.Add(new Path
            {
                Stroke = purple,
                StrokeThickness = 2.6,
                Data = Geometry.Parse("M13,9 V16 M19,9 V16")
            });
            return canvas;
        }

        var isYouTube = Platform == "YouTube";
        var box = new Border
        {
            Width = 28,
            Height = 22,
            CornerRadius = new CornerRadius(6),
            Background = BrushFromColor(isYouTube ? "#EA3546" : "#635BEE")
        };
        Canvas.SetTop(box, 3);
        canvas.Children.Add(box);
        canvas.Children.Add(new Path
        {
            Fill = Brushes.White,
            Data = Geometry.Parse(isYouTube
                ? "M11,9 L20,14 L11,19 Z"
                : "M5,14 C5,7 12,7 14,14 C16,21 23,21 23,14 C23,7 16,7 14,14 C12,21 5,21 5,14 Z")
        });
        return canvas;
    }

    private static BitmapFrame LoadSoopMark()
    {
        var decoder = BitmapDecoder.Create(new Uri("pack://application:,,,/Assets/Brand/soop-official.ico"),
            BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames.OrderByDescending(frame => frame.PixelWidth).First();
        frame.Freeze();
        return frame;
    }

    private static SolidColorBrush BrushFromColor(string color) =>
        new((Color)ColorConverter.ConvertFromString(color));
}
