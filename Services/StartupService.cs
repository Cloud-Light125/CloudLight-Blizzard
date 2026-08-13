using System.Diagnostics;
using Microsoft.Win32;

namespace CloudLightBlizzard.Services;

/// <summary>开机自启:读写 HKCU\...\Run。开启时带 --tray 参数,让开机启动直接进托盘。</summary>
public static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "CloudLight Blizzard";
    private static string LegacyValueName => "Bnet" + "Switch";

    /// <summary>当前 exe 的完整路径(单文件发布下是宿主 exe)。</summary>
    private static string ExePath =>
        Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule!.FileName;

    public static bool IsEnabled()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            return k?.GetValue(ValueName) is string s &&
                   s.Contains("CloudLight Blizzard.exe", StringComparison.OrdinalIgnoreCase) ||
                   k?.GetValue(LegacyValueName) is string;
        }
        catch { return false; }
    }

    public static void SetEnabled(bool on)
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(RunKey);
            if (k is null) return;
            if (on)
                k.SetValue(ValueName, $"\"{ExePath}\" --tray");
            else if (k.GetValue(ValueName) != null)
                k.DeleteValue(ValueName, throwOnMissingValue: false);
            if (k.GetValue(LegacyValueName) != null)
                k.DeleteValue(LegacyValueName, throwOnMissingValue: false);
        }
        catch { /* 写不了注册表就算了,不阻断 */ }
    }
}
