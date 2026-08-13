using System.Windows;

namespace BnetSwitch.Services;

/// <summary>运行时亮/暗主题切换:整体替换调色板字典,所有 DynamicResource 引用会自动刷新。</summary>
public static class ThemeManager
{
    public static bool IsDark { get; private set; }

    public static void Apply(bool dark)
    {
        IsDark = dark;
        var app = Application.Current;
        if (app is null) return;

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
    }
}
