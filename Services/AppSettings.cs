using System.IO;
using System.Text;
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
    private static readonly object PersistenceGate = new();
    private bool _persistenceBlocked;

    public string? ClientExe { get; set; }
    public bool CloseToTray { get; set; } = true;
    public bool CloseChoiceMade { get; set; }
    public bool StartMinimized { get; set; }
    public bool DarkMode { get; set; }
    public bool EnableProxy { get; set; }
    public string ProxyUrl { get; set; } = "";
    public bool FallbackDirect { get; set; }
    public bool SoopUseProxy { get; set; } = true;
    public bool YouTubeUseProxy { get; set; } = true;
    public bool TwitchUseProxy { get; set; } = true;
    public bool ShowAnnouncementBadge { get; set; } = true;
    public string CloudServiceBaseUrl { get; set; } = CloudServiceConfiguration.DefaultBaseUrl;
    public bool AutoStartSoop { get; set; }
    public bool AutoStartTwitch { get; set; }
    public bool BilibiliEnabled { get; set; }
    public bool BilibiliUseProxy { get; set; }
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
    public bool MigrationCompleted { get; set; }
    public string? SkippedUpdateVersion { get; set; }
    public DateTimeOffset? RemindAfter { get; set; }
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
        if (existing?._persistenceBlocked == true) return existing;
        var migration = existing?.MigrationCompleted == true
            ? new MigrationResult(false, false, false, Array.Empty<string>())
            : AppPaths.Current.MigrateLegacyData();
        var settings = LoadFrom(FilePath, rewrite: false);
        if (settings._persistenceBlocked) return settings;
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
        lock (PersistenceGate)
        {
            path = Path.GetFullPath(path);
            var backupPath = BackupPath(path);
            var primaryExists = File.Exists(path);
            var loadedFromBackup = false;
            AppSettings s;
            if (TryDeserialize(path, out var primary, out var primaryError))
            {
                s = primary!;
                EnsureBackup(path);
            }
            else if (TryDeserialize(backupPath, out var backup, out var backupError))
            {
                s = backup!;
                loadedFromBackup = true;
                WritePersistenceLog(path, "主设置读取失败，已从备份恢复。", primaryError);
            }
            else
            {
                s = new AppSettings { _persistenceBlocked = primaryExists };
                if (primaryExists)
                    WritePersistenceLog(path, "主设置与备份均无法读取，已阻止默认值覆盖原文件。",
                        primaryError ?? backupError);
            }

            Normalize(s);

            if (loadedFromBackup)
                RestorePrimaryFromBackup(path, backupPath, s);
            // 重写时会丢弃旧版本不再识别的字段。
            else if (rewrite && !s._persistenceBlocked)
                s.SaveToCore(path);
            return s;
        }
    }

    internal static string NormalizeMainSection(string? section) => section switch
    {
        "accounts" or "region" or "stats" or "drops" or "snapshots" or "diagnostics" or "settings" or "about" => section,
        // "overview" was removed in 2.1.0; old settings must open a real page.
        "overview" => "accounts",
        _ => "accounts",
    };

    public bool Save() => SaveTo(FilePath);

    public bool SaveTo(string path)
    {
        lock (PersistenceGate)
        {
            if (_persistenceBlocked)
            {
                WritePersistenceLog(path, "设置保存已阻止：启动时未能安全读取现有设置。", null);
                return false;
            }
            return SaveToCore(Path.GetFullPath(path));
        }
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

    private bool SaveToCore(string path)
    {
        string? temp = null;
        try
        {
            Normalize(this);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            temp = $"{path}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(this, JsonOptions));
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                       4096, FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(path))
            {
                try
                {
                    File.Replace(temp, path, BackupPath(path), ignoreMetadataErrors: true);
                }
                catch (PlatformNotSupportedException)
                {
                    ReplaceWithFallback(temp, path);
                }
                catch (IOException)
                {
                    ReplaceWithFallback(temp, path);
                }
            }
            else
            {
                File.Move(temp, path);
                EnsureBackup(path);
            }
            temp = null;
            return true;
        }
        catch (Exception ex)
        {
            WritePersistenceLog(path, "设置写入失败，原设置文件保持不变。", ex);
            return false;
        }
        finally
        {
            if (temp is not null)
                try { File.Delete(temp); } catch { }
        }
    }

    private static void ReplaceWithFallback(string temp, string path)
    {
        File.Copy(path, BackupPath(path), overwrite: true);
        File.Move(temp, path, overwrite: true);
    }

    private static bool TryDeserialize(string path, out AppSettings? settings, out Exception? error)
    {
        settings = null;
        error = null;
        if (!File.Exists(path)) return false;
        try
        {
            settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions);
            return settings is not null;
        }
        catch (Exception ex)
        {
            error = ex;
            return false;
        }
    }

    private static void RestorePrimaryFromBackup(string path, string backupPath, AppSettings settings)
    {
        try
        {
            if (File.Exists(path))
            {
                var quarantine = $"{path}.corrupt-{DateTimeOffset.Now:yyyyMMddHHmmssfff}";
                File.Move(path, quarantine);
            }
            if (!settings.SaveToCore(path))
                WritePersistenceLog(path, "备份已载入内存，但主设置文件恢复失败。", null);
            else if (!File.Exists(backupPath))
                File.Copy(path, backupPath, overwrite: false);
        }
        catch (Exception ex)
        {
            WritePersistenceLog(path, "备份已载入内存，但主设置文件恢复失败。", ex);
        }
    }

    private static void Normalize(AppSettings settings)
    {
        settings.HiddenAccountIds ??= new();
        settings.ExpiredAccountIds ??= new();
        settings.AccountPreferences ??= new();
        settings.ProxyUrl ??= "";
        settings.CloudServiceBaseUrl = CloudServiceConfiguration.NormalizeBaseUrl(settings.CloudServiceBaseUrl);
        settings.LastMainSection = NormalizeMainSection(settings.LastMainSection);
    }

    private static string BackupPath(string path) => path + ".bak";

    private static void EnsureBackup(string path)
    {
        var backup = BackupPath(path);
        if (File.Exists(path) && !File.Exists(backup))
        {
            try { File.Copy(path, backup, overwrite: false); }
            catch (Exception ex) { WritePersistenceLog(path, "无法创建设置备份。", ex); }
        }
    }

    private static void WritePersistenceLog(string settingsPath, string message, Exception? error)
    {
        try
        {
            var root = Path.GetDirectoryName(Path.GetFullPath(settingsPath))!;
            var logs = Path.Combine(root, "logs");
            Directory.CreateDirectory(logs);
            File.AppendAllText(Path.Combine(logs, "settings.log"),
                $"[{DateTimeOffset.Now:O}] {message}" +
                (error is null ? "" : $" {error.GetType().Name}: {error.Message}") + Environment.NewLine,
                Encoding.UTF8);
        }
        catch { }
    }
}
