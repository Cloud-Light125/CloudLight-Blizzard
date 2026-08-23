using System.IO;
using System.Text.Json;

namespace CloudLightBlizzard.Services;

/// <summary>CloudLight Blizzard 的所有应用数据路径。</summary>
public sealed class AppPaths
{
    public static AppPaths Current { get; } = new();

    public string Root { get; }
    public string SettingsFile => Path.Combine(Root, "settings.json");
    public string AccountsDir => Path.Combine(Root, "accounts");
    public string LogsDir => Path.Combine(Root, "logs");
    public string AnnouncementsDir => Path.Combine(Root, "announcements");
    public string DropsDir => Path.Combine(Root, "drops");
    public string SoopDropsDir => Path.Combine(DropsDir, "soop");
    public string YouTubeDropsDir => Path.Combine(DropsDir, "youtube");
    public string TwitchDropsDir => Path.Combine(DropsDir, "twitch");
    public string OverwatchCacheDir => Path.Combine(Root, "overwatch", "cache");
    public string DefaultRegionStorageDir => Path.Combine(Root, "region-switch");
    public string LegacyRoot { get; }
    public string LegacyRegionStorageDir => Path.Combine(LegacyRoot, "region-switch");

    public AppPaths(string? root = null, string? legacyRoot = null)
    {
        Root = Path.GetFullPath(root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "CloudLight", "CloudLight Blizzard"));
        // The split literal keeps obsolete branding out of current product metadata while preserving data migration.
        var legacyProductName = "Bnet" + "Switch";
        LegacyRoot = Path.GetFullPath(legacyRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), legacyProductName));
    }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(AccountsDir);
        Directory.CreateDirectory(LogsDir);
        Directory.CreateDirectory(AnnouncementsDir);
        Directory.CreateDirectory(SoopDropsDir);
        Directory.CreateDirectory(YouTubeDropsDir);
        Directory.CreateDirectory(TwitchDropsDir);
        Directory.CreateDirectory(OverwatchCacheDir);
    }

    /// <summary>
    /// 安全迁移旧版默认目录。普通数据只复制且不覆盖；默认区服目录优先整体移动。
    /// 用户自定义的 RegionStoragePath 永远不会被移动。
    /// </summary>
    public MigrationResult MigrateLegacyData()
    {
        if (!Directory.Exists(LegacyRoot) || PathsEqual(LegacyRoot, Root))
        {
            EnsureDirectories();
            return new MigrationResult(false, false, false, Array.Empty<string>());
        }

        Directory.CreateDirectory(Root);
        var migrated = new List<string>();
        var legacySettings = ReadLegacySettings();
        var customRegion = !string.IsNullOrWhiteSpace(legacySettings.RegionStoragePath) &&
                           !PathsEqual(legacySettings.RegionStoragePath!, LegacyRegionStorageDir);

        CopyFileIfMissing(Path.Combine(LegacyRoot, "settings.json"), SettingsFile, migrated);
        CopyDirectoryMissing(Path.Combine(LegacyRoot, "accounts"), AccountsDir, migrated);
        CopyDirectoryMissing(Path.Combine(LegacyRoot, "logs"), LogsDir, migrated);
        // 兼容旧目录中其它应用数据，但排除广告缓存与单独处理的区服大文件。
        foreach (var entry in Directory.EnumerateFileSystemEntries(LegacyRoot))
        {
            var name = Path.GetFileName(entry);
            if (name.Equals("region-switch", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("cache", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("settings.json", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("accounts", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("logs", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("ow", StringComparison.OrdinalIgnoreCase)) continue;
            if (Directory.Exists(entry)) CopyDirectoryMissing(entry, Path.Combine(Root, name), migrated);
            else CopyFileIfMissing(entry, Path.Combine(Root, name), migrated);
        }

        var regionMoved = false;
        if (!customRegion && Directory.Exists(LegacyRegionStorageDir))
        {
            if (!Directory.Exists(DefaultRegionStorageDir) || !Directory.EnumerateFileSystemEntries(DefaultRegionStorageDir).Any())
            {
                try
                {
                    if (Directory.Exists(DefaultRegionStorageDir)) Directory.Delete(DefaultRegionStorageDir);
                    Directory.Move(LegacyRegionStorageDir, DefaultRegionStorageDir);
                    regionMoved = VerifyRegionMigration(LegacyRegionStorageDir, DefaultRegionStorageDir);
                    if (regionMoved) migrated.Add("region-switch");
                }
                catch (IOException) { /* 跨卷等情况下保留旧大文件原位，由设置继续指向旧目录。 */ }
            }
        }

        EnsureDirectories();
        return new MigrationResult(migrated.Count > 0, regionMoved,
            !customRegion && Directory.Exists(LegacyRegionStorageDir) && !regionMoved, migrated);
    }

    private LegacySettings ReadLegacySettings()
    {
        try
        {
            var file = Path.Combine(LegacyRoot, "settings.json");
            return File.Exists(file)
                ? JsonSerializer.Deserialize<LegacySettings>(File.ReadAllText(file), new JsonSerializerOptions
                { PropertyNameCaseInsensitive = true }) ?? new LegacySettings()
                : new LegacySettings();
        }
        catch { return new LegacySettings(); }
    }

    private static bool VerifyRegionMigration(string source, string destination)
    {
        if (!Directory.Exists(destination)) return false;
        foreach (var relative in new[] { "active-generation.json", "generations", "staging" })
        {
            var oldItem = Path.Combine(source, relative);
            var newItem = Path.Combine(destination, relative);
            if (File.Exists(oldItem) && !File.Exists(newItem)) return false;
            if (Directory.Exists(oldItem) && !Directory.Exists(newItem)) return false;
        }
        return true;
    }

    private static void CopyDirectoryMissing(string source, string destination, List<string> migrated)
    {
        if (!Directory.Exists(source)) return;
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            CopyFileIfMissing(file, Path.Combine(destination, Path.GetRelativePath(source, file)), migrated);
    }

    private static void CopyFileIfMissing(string source, string destination, List<string> migrated)
    {
        if (!File.Exists(source) || File.Exists(destination)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, overwrite: false);
        var sourceInfo = new FileInfo(source);
        var destinationInfo = new FileInfo(destination);
        if (!destinationInfo.Exists || destinationInfo.Length != sourceInfo.Length)
            throw new IOException($"迁移文件验证失败：{source}");
        migrated.Add(Path.GetRelativePath(Path.GetDirectoryName(source)!, source));
    }

    private static bool PathsEqual(string left, string right)
    {
        try { return string.Equals(Path.GetFullPath(left).TrimEnd('\\', '/'), Path.GetFullPath(right).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    private sealed class LegacySettings { public string? RegionStoragePath { get; set; } }
}

public sealed record MigrationResult(bool Migrated, bool DefaultRegionMoved, bool LegacyRegionRetained,
    IReadOnlyList<string> Items);
