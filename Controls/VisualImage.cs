using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CloudLightBlizzard.Services.Visuals;

namespace CloudLightBlizzard.Controls;

/// <summary>Cancels unloaded/replaced requests; stale images never overwrite a newer reward.</summary>
public sealed class VisualImage : Grid
{
    public static readonly DependencyProperty SourceUrlProperty = DependencyProperty.Register(nameof(SourceUrl), typeof(string), typeof(VisualImage), new PropertyMetadata("", Changed));
    public static readonly DependencyProperty BrandProperty = DependencyProperty.Register(nameof(Brand), typeof(string), typeof(VisualImage), new PropertyMetadata("Twitch"));
    public static readonly DependencyProperty ImageStretchProperty = DependencyProperty.Register(nameof(ImageStretch), typeof(Stretch), typeof(VisualImage), new PropertyMetadata(Stretch.Uniform, StretchChanged));
    public string SourceUrl { get => (string)GetValue(SourceUrlProperty); set => SetValue(SourceUrlProperty, value); }
    public string Brand { get => (string)GetValue(BrandProperty); set => SetValue(BrandProperty, value); }
    public Stretch ImageStretch { get => (Stretch)GetValue(ImageStretchProperty); set => SetValue(ImageStretchProperty, value); }
    public string LoadState => _label.Text;
    internal VisualImageCache? ImageProvider { get; set; }
    internal static Func<string, CancellationToken, Task<VisualImageResult>>? ReviewLoader { get; set; }
    internal System.Windows.Media.Imaging.BitmapSource? LoadedImage => _image.Source as System.Windows.Media.Imaging.BitmapSource;
    private readonly Image _image = new() { Stretch = Stretch.Uniform };
    private readonly Viewbox _icon = new() { Width = 42, Height = 42, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _label = new() { Text = "官方未提供图片", FontSize = 12, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6, 10, 6, 0) };
    private readonly StackPanel _placeholder = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _retry = new() { Content = "↻", Width = 28, Height = 28, ToolTip = "重新加载图片", HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(4), Visibility = Visibility.Collapsed };
    private CancellationTokenSource? _request;
    public VisualImage()
    {
        SetResourceReference(BackgroundProperty, "App.SurfaceAlt");
        _label.SetResourceReference(TextBlock.ForegroundProperty, "Text.Secondary");
        var placeholder = new System.Windows.Shapes.Path { Data = Geometry.Parse("M2,2 H26 V26 H2 Z M4,22 L11,13 L17,19 L21,14 L25,22 M17,8 A3,3 0 1 0 23,8 A3,3 0 1 0 17,8"), StrokeThickness = 1.5, Fill = Brushes.Transparent };
        placeholder.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Text.Secondary"); _icon.Child = placeholder;
        _placeholder.Children.Add(_icon); _placeholder.Children.Add(_label); Children.Add(_placeholder); Children.Add(_image);
        _retry.SetResourceReference(StyleProperty, "IconButton"); _retry.Click += (_, _) => { (ImageProvider ?? VisualImageCache.ForBrand(Brand)).Invalidate(SourceUrl); Start(); }; Children.Add(_retry);
        Loaded += (_, _) => Start(); Unloaded += (_, _) => Cancel();
        SizeChanged += (_, _) =>
        {
            Clip = new RectangleGeometry(new Rect(0, 0, Math.Max(0, ActualWidth), Math.Max(0, ActualHeight)), 12, 12);
            _label.Visibility = ActualWidth < 100 || ActualHeight < 85 ? Visibility.Collapsed : Visibility.Visible;
            _icon.Width = _icon.Height = ActualWidth < 100 || ActualHeight < 85 ? 26 : 42;
        };
    }
    private static void Changed(DependencyObject d, DependencyPropertyChangedEventArgs e) { var c = (VisualImage)d; if (c.IsLoaded) c.Start(); }
    private static void StretchChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((VisualImage)d)._image.Stretch = (Stretch)e.NewValue;
    private void Cancel() { _request?.Cancel(); _request?.Dispose(); _request = null; }
    private async void Start()
    {
        Cancel(); var cts = new CancellationTokenSource(); _request = cts; _image.Source = null; _placeholder.Visibility = Visibility.Visible; _retry.Visibility = Visibility.Collapsed;
        _label.Text = string.IsNullOrWhiteSpace(SourceUrl) ? "官方未提供图片" : "正在加载图片…";
        try
        {
            var result = ReviewLoader is { } loader && SourceUrl.StartsWith("review-fixture://", StringComparison.Ordinal)
                ? await loader(SourceUrl, cts.Token) : await (ImageProvider ?? VisualImageCache.ForBrand(Brand)).LoadAsync(SourceUrl, cts.Token);
            if (!ReferenceEquals(_request, cts) || cts.IsCancellationRequested) return;
            _image.Source = result.Image; _label.Text = result.State; ToolTip = result.Image is null ? result.State : null; _placeholder.Visibility = result.Image is null ? Visibility.Visible : Visibility.Collapsed;
            _retry.Visibility = result.Image is null && VisualImageCache.IsPublicImageUri(SourceUrl, out _) ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (OperationCanceledException) { }
    }
}
