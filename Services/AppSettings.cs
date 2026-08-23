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
    public bool AutoStartSoop { get; set; }
    public bool AutoStartTwitch { get; set; }
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

        // 重写时会丢弃旧版本不再识别的字段。
        if (rewrite) s.SaveTo(path);
        return s;
    }

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
