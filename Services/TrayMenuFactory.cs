using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BnetSwitch.Services;

/// <summary>托盘右键菜单:跟随亮/暗主题着色 + Win11 DWM 圆角 + 圆角悬停高亮。</summary>
public static class TrayMenuFactory
{
    public static ContextMenuStrip Create(params (string text, EventHandler? onClick)[] items)
    {
        var menu = new ContextMenuStrip
        {
            ShowImageMargin = false,
            Font = new Font("Microsoft YaHei UI", 9.5f),
            Padding = new Padding(4),
        };
        foreach (var (text, onClick) in items)
        {
            if (text == "-") { menu.Items.Add(new ToolStripSeparator()); continue; }
            var mi = new ToolStripMenuItem(text) { Padding = new Padding(8, 3, 8, 3), AutoSize = true };
            if (onClick != null) mi.Click += onClick;
            menu.Items.Add(mi);
        }

        var renderer = new ThemedRenderer();
        menu.Renderer = renderer;
        menu.Opening += (_, _) => { renderer.Dark = ThemeManager.IsDark; ApplyColors(menu, renderer.Dark); };
        menu.Opened += (_, _) => RoundCorners(menu.Handle);
        return menu;
    }

    private static void ApplyColors(ContextMenuStrip menu, bool dark)
    {
        menu.BackColor = dark ? Color.FromArgb(0x26, 0x29, 0x33) : Color.White;
        menu.ForeColor = dark ? Color.FromArgb(0xEE, 0xF0, 0xF5) : Color.FromArgb(0x1A, 0x1F, 0x2E);
    }

    private static void RoundCorners(IntPtr hwnd)
    {
        try { int pref = 2 /* DWMWCP_ROUND */; DwmSetWindowAttribute(hwnd, 33, ref pref, sizeof(int)); }
        catch { /* Win10 无此属性,保持默认 */ }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private sealed class ThemedRenderer : ToolStripProfessionalRenderer
    {
        public bool Dark;

        public ThemedRenderer() : base(new ProfessionalColorTable { UseSystemColors = false }) { RoundedEdges = false; }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            var mi = e.Item as ToolStripMenuItem;
            if (!(e.Item.Selected || (mi?.Pressed ?? false))) return;

            var hover = Dark ? Color.FromArgb(0x33, 0x38, 0x45) : Color.FromArgb(0xF0, 0xF2, 0xF7);
            var rect = new Rectangle(3, 1, e.Item.Width - 6, e.Item.Height - 2);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = Rounded(rect, 6);
            using var b = new SolidBrush(hover);
            e.Graphics.FillPath(b, path);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = Dark ? Color.FromArgb(0xEE, 0xF0, 0xF5) : Color.FromArgb(0x1A, 0x1F, 0x2E);
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            var c = Dark ? Color.FromArgb(0x3A, 0x3F, 0x4D) : Color.FromArgb(0xEC, 0xEE, 0xF2);
            using var p = new Pen(c);
            int y = e.Item.Height / 2;
            e.Graphics.DrawLine(p, 8, y, e.Item.Width - 8, y);
        }

        // 不画默认灰色方框,交给 DWM 圆角边框。
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { }

        private static GraphicsPath Rounded(Rectangle r, int radius)
        {
            int d = radius * 2;
            var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }
}
