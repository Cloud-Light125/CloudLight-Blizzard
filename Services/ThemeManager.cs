using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace BnetSwitch.Services;

/// <summary>运行时亮/暗主题切换:整体替换调色板字典,所有 DynamicResource 引用会自动刷新。</summary>
public static class ThemeManager
{
    private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;
    private const int DwmwaUseImmersiveDarkMode = 20;

    public static bool IsDark { get; private set; }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute,
        ref int value, int valueSize);

    public static void Apply(bool dark)
    {
        IsDark = dark;
        var app = Application.Current;
        if (app is null) return;

        app.Activated -= OnApplicationActivated;
        app.Activated += OnApplicationActivated;

        var dicts = app.Resources.MergedDictionaries;
        for (int i = dicts.Count - 1; i >= 0; i--)
        {
            var s = dicts[i].Source?.OriginalString ?? "";
            if (s.Contains("Palette.", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("StatsColors.", StringComparison.OrdinalIgnoreCase))
                dicts.RemoveAt(i);
        }

        string tone = dark ? "Dark" : "Light";
        dicts.Add(new ResourceDictionary { Source = new Uri($"pack://application:,,,/Themes/Palette.{tone}.xaml") });
        dicts.Add(new ResourceDictionary { Source = new Uri($"pack://application:,,,/Stats/Theme/StatsColors.{tone}.xaml") });

        foreach (Window window in app.Windows)
            ApplyTitleBar(window, dark);
    }

    /// <summary>让窗口标题栏跟随应用主题，并在 HWND 创建后应用初始状态。</summary>
    public static void Attach(Window window)
    {
        window.SourceInitialized += (_, _) => ApplyTitleBar(window, IsDark);
    }

    private static void ApplyTitleBar(Window window, bool dark)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;

        int enabled = dark ? 1 : 0;
        if (DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode,
            ref enabled, sizeof(int)) != 0)
            DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkModeBefore20H1,
                ref enabled, sizeof(int));
    }

    private static void OnApplicationActivated(object? sender, EventArgs e)
    {
        if (Application.Current is not { } app) return;
        foreach (Window window in app.Windows)
            ApplyTitleBar(window, IsDark);
    }
}
