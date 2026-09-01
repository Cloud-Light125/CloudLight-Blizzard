using System.IO;
using System.Text.Json;
using CloudLightBlizzard.Models;
using CloudLightBlizzard.Services.OverwatchRegion;

namespace CloudLightBlizzard.Services;

/// <summary>
/// CloudLight Blizzard 的本地设置。
/// 存储位置：文档\CloudLight\CloudLight Blizzard\settings.json。
/// </summary>
public sealed class AppSettings
{
    public string? ClientExe { get; set; }
    public bool CloseToTray { get; set; } = true;
    public bool CloseChoiceMade { get; set; }
    public bool StartMinimized { get; set; }
    public bool DarkMode { get; set; }
    public bool EnableProxy { get; set; }
    public string ProxyUrl { get; set; } = "";
    public bool FallbackDirect { get; set; }
    public bool ShowAnnouncementBadge { get; set; } = true;
    public string CloudServiceBaseUrl { get; set; } = CloudServiceConfiguration.DefaultBaseUrl;
    public bool AutoStartSoop { get; set; }
    public bool AutoStartTwitch { get; set; }
    public bool BilibiliEnabled { get; set; }
    public bool AutoStartBilibili { get; set; }
    public bool AutoResumeBilibiliDrops { get; set; }
    public string BilibiliUserName { get; set; } = "";
    public long BilibiliUid { get; set; }
    /// <summary>CurrentUser DPAPI blob; this is never a plaintext Cookie.</summary>
    public string BilibiliCredentialBlob { get; set; } = "";
    public List<long> BilibiliRoomIds { get; set; } = new();
    public List<string> BilibiliTaskIds { get; set; } = new();
    public string BilibiliWatchMode { get; set; } = "standard";
    public int BilibiliSessionsPerRoom { get; set; } = 1;
    public int BilibiliReconnectDelaySeconds { get; set; } = 8;
    public int BilibiliTaskIntervalSeconds { get; set; } = 30;
    public bool BilibiliAutoClaim { get; set; }
    public bool BilibiliTaskNotifications { get; set; } = true;
    public double WindowWidth { get; set; }
    public double WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }
    public List<long> HiddenAccountIds { get; set; } = new();
    public List<long> ExpiredAccountIds { get; set; } = new();
    public string? OverwatchGamePath { get; set; }
    public string? RegionStoragePath { get; set; }
    public RegionBackupMode RegionBackupMode { get; set; } = RegionBackupMode.VerifiedDifference;
    public bool Step4ReminderIgnored { get; set; }
    public bool MigrationCompleted { get; set; }
    public string? SkippedUpdateVersion { get; set; }
    public DateTimeOffset? RemindAfter { get; set; }
    public UpdateChannel UpdateChannel { get; set; } = UpdateChannel.Stable;
    public DateTimeOffset? LastUpdateCheckAt { get; set; }
    public string? LastUpdateFailure { get; set; }
    public string? LastSuccessfulUpdateFrom { get; set; }
    public string? LastSuccessfulUpdateTo { get; set; }
    public DateTimeOffset? LastSuccessfulUpdateAt { get; set; }
    public bool EnableWindowsNotifications { get; set; } = true;
    public bool NotifyRegionSwitch { get; set; } = true;
    public bool NotifyDrops { get; set; } = true;
    public bool NotifyUpdates { get; set; } = true;
    public bool NotifyAnnouncements { get; set; } = true;
    public string LastMainSection { get; set; } = "accounts";
    public Dictionary<string, AccountPreference> AccountPreferences { get; set; } = new();

    public static string FilePath
    {
        get
        {
            AppPaths.Current.EnsureDirectories();
            return AppPaths.Current.SettingsFile;
        }
    }

    public static AppSettings Load()
    {
        var existing = File.Exists(FilePath) ? LoadFrom(FilePath, rewrite: false) : null;
        var migration = existing?.MigrationCompleted == true
            ? new MigrationResult(false, false, false, Array.Empty<string>())
            : AppPaths.Current.MigrateLegacyData();
        var settings = LoadFrom(FilePath, rewrite: false);
        if (!settings.MigrationCompleted || migration.Migrated)
        {
            settings.MigrationCompleted = true;
            if (migration.LegacyRegionRetained && string.IsNullOrWhiteSpace(settings.RegionStoragePath))
                settings.RegionStoragePath = AppPaths.Current.LegacyRegionStorageDir;
            if (!string.IsNullOrWhiteSpace(settings.RegionStoragePath) &&
                migration.DefaultRegionMoved &&
                string.Equals(Path.GetFullPath(settings.RegionStoragePath).TrimEnd('\\', '/'),
                    Path.GetFullPath(AppPaths.Current.LegacyRegionStorageDir).TrimEnd('\\', '/'),
                    StringComparison.OrdinalIgnoreCase))
                settings.RegionStoragePath = null;
            settings.SaveTo(FilePath);
        }
        return settings;
    }

    public static AppSettings LoadFrom(string path, bool rewrite = false)
    {
        AppSettings s;
        try
        {
            s = (File.Exists(path)
                    ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions)
                    : null)
                ?? new AppSettings();
        }
        catch
        {
            s = new AppSettings();
        }

        s.HiddenAccountIds ??= new();
        s.ExpiredAccountIds ??= new();
        s.AccountPreferences ??= new();
        s.ProxyUrl ??= "";
        s.CloudServiceBaseUrl = CloudServiceConfiguration.NormalizeBaseUrl(s.CloudServiceBaseUrl);
        s.LastMainSection = NormalizeMainSection(s.LastMainSection);

        // 重写时会丢弃旧版本不再识别的字段。
        if (rewrite) s.SaveTo(path);
        return s;
    }

    internal static string NormalizeMainSection(string? section) => section switch
    {
        "accounts" or "region" or "stats" or "drops" or "snapshots" or "diagnostics" or "settings" or "about" => section,
        // "overview" was removed in 2.1.0; old settings must open a real page.
        "overview" => "accounts",
        _ => "accounts",
    };

    public void Save()
    {
        SaveTo(FilePath);
    }

    public void SaveTo(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, JsonOptions));
            File.Move(temp, path, overwrite: true);
        }
        catch { }
    }

    public AccountPreference PreferenceFor(long accountId)
    {
        var key = accountId.ToString();
        if (!AccountPreferences.TryGetValue(key, out var value))
            AccountPreferences[key] = value = new AccountPreference();
        return value;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };
}
