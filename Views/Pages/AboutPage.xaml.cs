using System.Windows.Controls;
using System.Diagnostics;
using System.Windows;
using System.IO;
using CloudLightBlizzard.ViewModels;

namespace CloudLightBlizzard.Views.Pages;

public sealed class ThirdPartyComponentInfo
{
    public ThirdPartyComponentInfo(string name, string purpose, string sourceUrl, string sourceOpenUrl,
        string license, string versionInfo, string distributionInfo, string licenseLocation,
        string noticeLocation, string licenseOpenPath = "", string noticeOpenPath = "")
    {
        Name = name;
        Purpose = purpose;
        SourceUrl = sourceUrl;
        SourceOpenUrl = sourceOpenUrl;
        License = license;
        VersionInfo = versionInfo;
        DistributionInfo = distributionInfo;
        LicenseLocation = licenseLocation;
        NoticeLocation = noticeLocation;
        LicenseOpenPath = licenseOpenPath;
        NoticeOpenPath = noticeOpenPath;
    }

    public string Name { get; }
    public string Purpose { get; }
    public string SourceUrl { get; }
    public string SourceOpenUrl { get; }
    public string License { get; }
    public string VersionInfo { get; }
    public string DistributionInfo { get; }
    public string LicenseLocation { get; }
    public string NoticeLocation { get; }
    public string LicenseOpenPath { get; }
    public string NoticeOpenPath { get; }
    public bool HasSourceOpenUrl => !string.IsNullOrWhiteSpace(SourceOpenUrl);
    public bool HasLicenseOpenPath => !string.IsNullOrWhiteSpace(LicenseOpenPath);
    public bool HasNoticeOpenPath => !string.IsNullOrWhiteSpace(NoticeOpenPath);
}

public partial class AboutPage : UserControl
{
    public IReadOnlyList<ThirdPartyComponentInfo> Components { get; } =
    [
        new(
            "BiliBiliDropsMiner",
            "哔哩哔哩掉宝 Worker 使用的非 GUI 核心，包含二维码登录、任务与奖励接口、WBI 签名和 x25Kn 观看 Session。",
            "https://github.com/mi0e/BiliBiliDropsMiner",
            "https://github.com/mi0e/BiliBiliDropsMiner",
            "MIT License",
            "上游 commit a0d8bd51728aabaef66c651613324adba15d9ce8",
            "核心源码位于 vendor/bilibili_drops_miner/，随 bilibili-worker.exe 分发。",
            "许可证：THIRD_PARTY_LICENSES/BiliBiliDropsMiner-MIT.txt",
            "声明：THIRD_PARTY_NOTICES.md；依赖清单：THIRD_PARTY_LICENSES/Bilibili/README.md",
            "THIRD_PARTY_LICENSES/BiliBiliDropsMiner-MIT.txt",
            "THIRD_PARTY_NOTICES.md"),
        new(
            "Bilibili Worker runtime",
            "Bilibili Worker 的 HTTP、二维码和图像运行时依赖；只保留当前 Worker 构建实际使用的直接与传递依赖。",
            "https://pypi.org/project/httpx/ · https://pypi.org/project/qrcode/ · https://pypi.org/project/Pillow/",
            "https://pypi.org/project/httpx/",
            "BSD-3-Clause / BSD / HPND；传递依赖含 MIT、BSD-3-Clause、MPL-2.0",
            "httpx 0.28.1 · httpcore 1.0.9 · qrcode 8.2 · Pillow 11.3.0；其余版本见依赖清单",
            "由 PyInstaller 打包进 bilibili-worker.exe；精确许可证文本复制到 THIRD_PARTY_LICENSES/Bilibili/。",
            "许可证目录：THIRD_PARTY_LICENSES/Bilibili/",
            "依赖清单：THIRD_PARTY_LICENSES/Bilibili/README.md；总声明：THIRD_PARTY_NOTICES.md",
            "THIRD_PARTY_LICENSES/Bilibili",
            "THIRD_PARTY_LICENSES/Bilibili/README.md"),
        new(
            "TwitchDropsMiner-NoAutoClaim / DevilXD TwitchDropsMiner",
            "Twitch 掉宝的无 GUI 核心与 headless Worker，负责活动、背包、频道、HTTP/GQL 和 WebSocket 业务。",
            "https://github.com/yundan125/TwitchDropsMiner-NoAutoClaim",
            "https://github.com/yundan125/TwitchDropsMiner-NoAutoClaim",
            "MIT License",
            "基于上游 16.dev；当前补丁说明保存在 Twitch core。",
            "核心源码与语言资源作为 twitch-worker.exe 的内部资源分发。",
            "许可证：THIRD_PARTY_LICENSES/TwitchDropsMiner-MIT.txt",
            "声明：THIRD_PARTY_NOTICES.md",
            "THIRD_PARTY_LICENSES/TwitchDropsMiner-MIT.txt",
            "THIRD_PARTY_NOTICES.md"),
        new(
            "Drops Worker Python runtime",
            "SOOP、YouTube 与 Twitch Worker 使用的 Python 运行时依赖；不把没有确认许可证的上游项目列为可再分发组件。",
            "https://pypi.org/",
            "https://pypi.org/",
            "aiohttp Apache-2.0 AND MIT · yarl / requests / websocket-client Apache-2.0 · yt-dlp Public Domain · truststore MIT",
            "YouTube 固定 requests 2.34.2、yt-dlp 2026.7.4、websocket-client 1.9.0；SOOP/Twitch 依 requirements 约束构建。",
            "由 build-workers.ps1 生成并打包进对应的单文件 Worker；PyInstaller 仅作为构建/bootloader 组件使用。",
            "许可证文本：随各 Worker 构建依赖提供；本仓库未逐一复制全部包文本",
            "登记：THIRD_PARTY_NOTICES.md 的 Python runtime components",
            "",
            "THIRD_PARTY_NOTICES.md"),
        new(
            "Microsoft.Data.Sqlite",
            "应用本地 SQLite 数据存储，用于账号、设置和运行数据持久化。",
            "https://www.nuget.org/packages/Microsoft.Data.Sqlite/8.0.7",
            "https://www.nuget.org/packages/Microsoft.Data.Sqlite/8.0.7",
            "MIT License",
            "NuGet package 8.0.7",
            "由 CloudLight Blizzard.csproj 引用，并随应用发布目录保留其运行时程序集。",
            "许可证：遵循 NuGet package metadata",
            "声明：THIRD_PARTY_NOTICES.md",
            "",
            "THIRD_PARTY_NOTICES.md"),
        new(
            "CommunityToolkit.WinUI.Notifications",
            "Windows Toast 通知支持，用于应用公告和后台状态提醒。",
            "https://www.nuget.org/packages/CommunityToolkit.WinUI.Notifications/7.1.2",
            "https://github.com/CommunityToolkit/WindowsCommunityToolkit",
            "MIT License",
            "NuGet package 7.1.2",
            "由 CloudLight Blizzard.csproj 引用，并随应用发布目录保留其运行时程序集。",
            "许可证：遵循 NuGet package metadata",
            "声明：THIRD_PARTY_NOTICES.md",
            "",
            "THIRD_PARTY_NOTICES.md"),
    ];

    public AboutPage() => InitializeComponent();
    public void Initialize(MainViewModel main) => DataContext = main;

    private void OnOpenSource(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string url } && Uri.TryCreate(url, UriKind.Absolute, out _))
            OpenPath(url);
    }

    private void OnOpenLocalPath(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string relativePath } || string.IsNullOrWhiteSpace(relativePath)) return;
        var path = Path.Combine(AppContext.BaseDirectory,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            MessageBox.Show($"当前发布目录中未找到：{relativePath}", "打开第三方文件",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        OpenPath(path);
    }

    private static void OpenPath(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch { }
    }
}
