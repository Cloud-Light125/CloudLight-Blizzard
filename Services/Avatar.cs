using System.Windows.Media;

namespace BnetSwitch.Services;

/// <summary>按账号 id 稳定地生成一对头像配色(半透明同色底 + 饱和字),亮/暗主题都能看清。</summary>
public static class Avatar
{
    public static (Brush bg, Brush fg) For(long id)
    {
        var hue = (int)(((id % 360) + 360) % 360);
        var fg = Hsl(hue, 0.55, 0.60);
        var bgBase = Hsl(hue, 0.55, 0.55);
        var bg = Color.FromArgb(0x33, bgBase.R, bgBase.G, bgBase.B);   // ~20% 半透明,叠在亮/暗卡片上都成立

        var fb = new SolidColorBrush(fg); fb.Freeze();
        var bb = new SolidColorBrush(bg); bb.Freeze();
        return (bb, fb);
    }

    private static Color Hsl(double h, double s, double l)
    {
        h /= 360.0;
        double r, g, b;
        if (s == 0) { r = g = b = l; }
        else
        {
            double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            double p = 2 * l - q;
            r = Hue(p, q, h + 1.0 / 3);
            g = Hue(p, q, h);
            b = Hue(p, q, h - 1.0 / 3);
        }
        return Color.FromRgb((byte)Math.Round(r * 255), (byte)Math.Round(g * 255), (byte)Math.Round(b * 255));
    }

    private static double Hue(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1.0 / 6) return p + (q - p) * 6 * t;
        if (t < 1.0 / 2) return q;
        if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
        return p;
    }
}
