using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CloudLightBlizzard.Services.Visuals;

/// <summary>Explicit --ui-review layout fixtures. These drawings are never official reward metadata.</summary>
internal static class ReviewImageAssets
{
    internal static Task<VisualImageResult> LoadAsync(string source,CancellationToken ct)=>Task.Run(()=>
    {
        ct.ThrowIfCancellationRequested();
        var cover=source.EndsWith("cover",StringComparison.Ordinal);
        var second=source.EndsWith("benefit-b",StringComparison.Ordinal);
        var drawing=new DrawingVisual();
        using(var dc=drawing.RenderOpen())
        {
            dc.DrawRoundedRectangle(new LinearGradientBrush(Color.FromRgb(28,35,67),Color.FromRgb(64,54,108),45),null,new Rect(0,0,480,360),20,20);
            var accent=cover?Color.FromRgb(255,190,85):second?Color.FromRgb(101,218,207):Color.FromRgb(176,153,255);
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(25,accent.R,accent.G,accent.B)),null,new Point(240,165),145,125);
            dc.DrawGeometry(new LinearGradientBrush(Colors.White,accent,90),new Pen(new SolidColorBrush(accent),3),Geometry.Parse(cover?"M180,185 L240,100 L300,185 L272,185 L240,140 L208,185 Z":second?"M240,60 L320,110 L320,210 L240,258 L160,210 L160,110 Z":"M240,55 L315,125 L285,215 L240,260 L195,215 L165,125 Z"));
            if(!cover)dc.DrawGeometry(null,new Pen(new SolidColorBrush(Color.FromArgb(125,70,55,125)),3),Geometry.Parse("M240,55 L240,260 M165,125 L240,150 L315,125 M195,215 L240,150 L285,215"));
            var label=cover?"测试游戏封面":second?"模拟物品 B":"模拟物品 A";
            dc.DrawText(new FormattedText(label,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Microsoft YaHei UI"),23,Brushes.White,1),new Point(32,286));
            dc.DrawText(new FormattedText("隔离布局素材 · 非官方物品",CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Microsoft YaHei UI"),14,new SolidColorBrush(Color.FromRgb(195,190,215)),1),new Point(32,325));
        }
        var bitmap=new RenderTargetBitmap(480,360,96,96,PixelFormats.Pbgra32);bitmap.Render(drawing);bitmap.Freeze();
        return new VisualImageResult(bitmap,"隔离测试图片 · 非官方物品");
    },ct);
}
