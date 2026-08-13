using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CloudLightBlizzard.Controls;

/// <summary>
/// 极简矢量图标控件:把设计稿的 SVG 描边路径转成 WPF Geometry,按需描边/填充、可跟主题变色。
/// 几何坐标沿用设计稿的居中坐标系(-16..16),外面套 Viewbox 自动缩放(连描边一起等比缩放)。
/// </summary>
public sealed class IconView : Control
{
    static IconView()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(IconView),
            new FrameworkPropertyMetadata(typeof(IconView)));
    }

    public static readonly DependencyProperty GeometryProperty =
        DependencyProperty.Register(nameof(Geometry), typeof(Geometry), typeof(IconView));

    public static readonly DependencyProperty StrokeProperty =
        DependencyProperty.Register(nameof(Stroke), typeof(Brush), typeof(IconView));

    public static readonly DependencyProperty FillProperty =
        DependencyProperty.Register(nameof(Fill), typeof(Brush), typeof(IconView));

    public static readonly DependencyProperty IconSizeProperty =
        DependencyProperty.Register(nameof(IconSize), typeof(double), typeof(IconView),
            new FrameworkPropertyMetadata(20.0));

    public static readonly DependencyProperty ThicknessProperty =
        DependencyProperty.Register(nameof(Thickness), typeof(double), typeof(IconView),
            new FrameworkPropertyMetadata(2.0));

    /// <summary>图标几何(描边路径,坐标 -16..16)。</summary>
    public Geometry? Geometry
    {
        get => (Geometry?)GetValue(GeometryProperty);
        set => SetValue(GeometryProperty, value);
    }

    /// <summary>描边色(留空则不描边)。</summary>
    public Brush? Stroke
    {
        get => (Brush?)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    /// <summary>填充色(留空则不填充)。用于实心图标,如播放三角。</summary>
    public Brush? Fill
    {
        get => (Brush?)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    /// <summary>显示尺寸(正方形边长)。</summary>
    public double IconSize
    {
        get => (double)GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    /// <summary>描边粗细(相对 32 单位坐标系,会被 Viewbox 一起缩放)。</summary>
    public double Thickness
    {
        get => (double)GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }
}
