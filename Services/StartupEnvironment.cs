using System.Runtime.CompilerServices;

namespace CloudLightBlizzard.Services;

/// <summary>在 WPF 初始化字体缓存前补齐少数启动器可能遗漏的进程环境变量。</summary>
internal static class StartupEnvironment
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WINDIR")))
            return;

        var systemRoot = Environment.GetEnvironmentVariable("SystemRoot");
        if (!string.IsNullOrWhiteSpace(systemRoot))
            Environment.SetEnvironmentVariable("WINDIR", systemRoot,
                EnvironmentVariableTarget.Process);
    }
}
