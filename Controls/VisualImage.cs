using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CloudLightBlizzard.Services.Visuals;

namespace CloudLightBlizzard.Controls;

/// <summary>Cancels unloaded/replaced requests; stale images never overwrite a newer reward.</summary>
public sealed class VisualImage : Grid
{
    public static readonly DependencyProperty SourceUrlProperty = DependencyProperty.Register(nameof(SourceUrl), typeof(string), typeof(VisualImage), new PropertyMetadata("", Changed));
    public static readonly DependencyProperty BrandProperty = DependencyProperty.Register(nameof(Brand), typeof(string), typeof(VisualImage), new PropertyMetadata("Twitch"));
    public static readonly DependencyProperty DecodePixelWidthProperty = DependencyProperty.Register(nameof(DecodePixelWidth),typeof(int),typeof(VisualImage),new PropertyMetadata(640,Changed));
    public int DecodePixelWidth {get=>(int)GetValue(DecodePixelWidthProperty);set=>SetValue(DecodePixelWidthProperty,value);}
    public static readonly DependencyProperty ImageStretchProperty = DependencyProperty.Register(nameof(ImageStretch), typeof(Stretch), typeof(VisualImage), new PropertyMetadata(Stretch.Uniform, StretchChanged));
    public string SourceUrl { get => (string)GetValue(SourceUrlProperty); set => SetValue(SourceUrlProperty, value); }
    public string Brand { get => (string)GetValue(BrandProperty); set => SetValue(BrandProperty, value); }
    public Stretch ImageStretch { get => (Stretch)GetValue(ImageStretchProperty); set => SetValue(ImageStretchProperty, value); }
    public string LoadState => _label.Text;
    internal VisualImageCache? ImageProvider { get; set; }
    internal static Func<string, CancellationToken, Task<VisualImageResult>>? ReviewLoader { get; set; }
    internal System.Windows.Media.Imaging.BitmapSource? LoadedImage => _image.Source as System.Windows.Media.Imaging.BitmapSource;
    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(
        nameof(Placeholder), typeof(string), typeof(VisualImage), new PropertyMetadata("", (d, _) => ((VisualImage)d).UpdatePlaceholder()));
    public string Placeholder { get => (string)GetValue(PlaceholderProperty); set => SetValue(PlaceholderProperty, value); }

    private readonly System.Windows.Shapes.Path _placeholderGlyph = new() { StrokeThickness = 1.5 };
    private readonly Image _image = new() { Stretch = Stretch.Uniform };
    private readonly Viewbox _icon = new() { Width = 42, Height = 42, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _label = new() { Text = "官方未提供图片", FontSize = 12, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6, 10, 6, 0) };
    private readonly StackPanel _placeholder = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _retry = new() { Content = "↻", Width = 28, Height = 28, ToolTip = "重新加载图片", HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(4), Visibility = Visibility.Collapsed };
    private CancellationTokenSource? _request;
    private readonly List<ScrollViewer> _viewports=[];
    private readonly DispatcherTimer _viewportTimer=new(){Interval=TimeSpan.FromMilliseconds(100)};
    private string _displayedSource="";
    public VisualImage()
    {
        SetResourceReference(BackgroundProperty, "App.SurfaceAlt");
        _label.SetResourceReference(TextBlock.ForegroundProperty, "Text.Secondary");
        _icon.Child = _placeholderGlyph;
        UpdatePlaceholder();
        _placeholder.Children.Add(_icon); _placeholder.Children.Add(_label); Children.Add(_placeholder); Children.Add(_image);
        _retry.SetResourceReference(StyleProperty, "IconButton"); _retry.Click += (_, _) => { (ImageProvider ?? VisualImageCache.ForBrand(Brand)).Invalidate(SourceUrl); Start(); }; Children.Add(_retry);
        _viewportTimer.Tick+=(_,_)=>{_viewportTimer.Stop();EvaluateViewport();};
        Loaded += (_, _) =>
        {
            for(DependencyObject? parent=VisualTreeHelper.GetParent(this);parent is not null;parent=VisualTreeHelper.GetParent(parent))
                if(parent is ScrollViewer scroll){_viewports.Add(scroll);scroll.ScrollChanged+=ViewportScrolled;}
            ScheduleViewport();
        };
        Unloaded += (_, _) =>
        {
            foreach(var viewport in _viewports)viewport.ScrollChanged-=ViewportScrolled;
            _viewports.Clear();_viewportTimer.Stop();Cancel();_image.Source=null;_displayedSource="";
        };
        IsVisibleChanged+=(_,_)=>ScheduleViewport();
        SizeChanged += (_, _) =>
        {
            Clip = new RectangleGeometry(new Rect(0, 0, Math.Max(0, ActualWidth), Math.Max(0, ActualHeight)), 12, 12);
            UpdatePlaceholder();
            ScheduleViewport();
        };
    }
    private void UpdatePlaceholder()
    {
        var account = Placeholder == "Account";
        var reward = Placeholder == "Reward";
        _placeholderGlyph.Data = Geometry.Parse(Placeholder switch
        {
            "Account" => "M8,7 A4,4 0 1 0 16,7 A4,4 0 1 0 8,7 M3,22 V20 Q3,15 12,15 Q21,15 21,20 V22 Z",
            "Reward" => "M3,10 H21 V14 H3 Z M5,14 V22 H19 V14 M12,10 V22 M12,10 C1,10 5,1 9,5 Z M12,10 C23,10 19,1 15,5 Z",
            _ => "M2,2 H26 V26 H2 Z M4,22 L11,13 L17,19 L21,14 L25,22 M17,8 A3,3 0 1 0 23,8 A3,3 0 1 0 17,8",
        });
        _placeholderGlyph.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, account ? "Accent.Primary" : "Text.Secondary");
        SetResourceReference(BackgroundProperty, account ? "AccSoft" : "App.SurfaceAlt");
        _label.Visibility = !account && (reward || (ActualWidth >= 100 && ActualHeight >= 85)) ? Visibility.Visible : Visibility.Collapsed;
        _label.FontSize = reward ? 10 : 12;
        _label.Margin = reward ? new Thickness(3, 4, 3, 0) : new Thickness(6, 10, 6, 0);
        _icon.Width = _icon.Height = account ? 22 : reward ? 20 : ActualWidth < 100 || ActualHeight < 85 ? 26 : 42;
    }

    private static void Changed(DependencyObject d, DependencyPropertyChangedEventArgs e) { var c = (VisualImage)d;c.Cancel();c._image.Source=null;c._displayedSource="";if(c.IsLoaded)c.ScheduleViewport(); }
    private static void StretchChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((VisualImage)d)._image.Stretch = (Stretch)e.NewValue;
    private void Cancel() { _request?.Cancel(); _request?.Dispose(); _request = null; }
    private void ViewportScrolled(object sender,ScrollChangedEventArgs e)=>ScheduleViewport();
    private void ScheduleViewport(){if(IsLoaded){_viewportTimer.Stop();_viewportTimer.Start();}}
    private void EvaluateViewport()
    {
        var visible=IsVisible&&ActualWidth>0&&ActualHeight>0&&_viewports.All(viewport=>
        {
            try{return TransformToAncestor(viewport).TransformBounds(new Rect(0,0,ActualWidth,ActualHeight)).IntersectsWith(new Rect(0,0,viewport.ActualWidth,viewport.ActualHeight));}
            catch(InvalidOperationException){return false;}
        });
        if(!visible){Cancel();return;}
        if(_displayedSource==SourceUrl&&_image.Source is not null||_request is not null)return;
        Start();
    }
    private async void Start()
    {
        Cancel(); var cts = new CancellationTokenSource(); _request = cts; _image.Source = null; _placeholder.Visibility = Visibility.Visible; _retry.Visibility = Visibility.Collapsed;
        _label.Text = string.IsNullOrWhiteSpace(SourceUrl) ? "官方未提供图片" : "正在加载图片…";
        try
        {
            var result = ReviewLoader is { } loader && SourceUrl.StartsWith("review-fixture://", StringComparison.Ordinal)
                ? await loader(SourceUrl, cts.Token) : await (ImageProvider ?? VisualImageCache.ForBrand(Brand)).LoadAsync(SourceUrl, cts.Token,DecodePixelWidth);
            if (!ReferenceEquals(_request, cts) || cts.IsCancellationRequested) return;
            _image.Source = result.Image;
            _label.Text = Placeholder == "Reward" && string.IsNullOrWhiteSpace(SourceUrl) ? "暂无官方图片" : result.State; ToolTip = result.Image is null ? result.State : null; _placeholder.Visibility = result.Image is null ? Visibility.Visible : Visibility.Collapsed;
            _displayedSource=SourceUrl;
            _retry.Visibility = result.Image is null && VisualImageCache.IsPublicImageUri(SourceUrl, out _) ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (OperationCanceledException) { }
    }
}
