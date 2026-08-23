using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace CloudLightBlizzard.Services.OverwatchRegion;

public sealed class OverwatchRegionScanner
{
    private const int ScanRetryCount = 2;
    private static readonly string[] IgnoredRuntimePrefixes =
    {
        "cache/", "logs/", "log/", "temp/", "tmp/", "shadercache/", "shader-cache/",
        "crash/", "crashes/", "crashdumps/", "crash-reports/", "telemetry/",
        // ecache is the mutable CASC encoding cache. Keep data/config/indices/pro and all packaged CASC data.
        "data/casc/ecache/",
        "_retail_/cache/", "_retail_/logs/", "_retail_/log/", "_retail_/temp/", "_retail_/tmp/",
        "_retail_/shadercache/", "_retail_/shader-cache/", "_retail_/crash/", "_retail_/crashes/",
        "_retail_/crashdumps/", "_retail_/crash-reports/", "_retail_/telemetry/",
    };

    public Task WaitForQuiescenceAsync(string gameRoot, IProgress<RegionProgress>? progress = null,
        CancellationToken cancellationToken = default, int observationMilliseconds = 6000) =>
        Task.Run(() => WaitForQuiescence(gameRoot, progress, cancellationToken, observationMilliseconds), cancellationToken);

    public Task<OverwatchRegionManifest> ScanAsync(string gameRoot, OverwatchRegion region,
        IProgress<RegionProgress>? progress = null, CancellationToken cancellationToken = default) =>
        Task.Run(() => Scan(gameRoot, region, progress, cancellationToken), cancellationToken);

    public Task<RegionScanResult> ScanBestEffortAsync(string gameRoot, OverwatchRegion region,
        IProgress<RegionProgress>? progress = null, CancellationToken cancellationToken = default) =>
        Task.Run(() => ScanBestEffort(gameRoot, region, progress, cancellationToken), cancellationToken);

    private static void WaitForQuiescence(string root, IProgress<RegionProgress>? progress, CancellationToken token,
        int observationMilliseconds)
    {
        root = Path.GetFullPath(root);
        var started = Environment.TickCount64;
        var stableSince = started;
        var previous = CaptureDirectoryStamp(root);
        while (Environment.TickCount64 - started < Math.Max(observationMilliseconds * 2L, 3000L))
        {
            token.ThrowIfCancellationRequested();
            Thread.Sleep(500);
            var current = CaptureDirectoryStamp(root);
            if (!string.Equals(previous, current, StringComparison.Ordinal))
            {
                previous = current;
                stableSince = Environment.TickCount64;
            }
            else if (Environment.TickCount64 - stableSince >= observationMilliseconds)
            {
                return;
            }
            var elapsed = (int)Math.Min(observationMilliseconds, Environment.TickCount64 - stableSince);
            progress?.Report(new RegionProgress("正在确认 Battle.net 已完成游戏文件更新…", elapsed,
                observationMilliseconds));
        }
        throw new IOException("Battle.net 仍在更新或整理游戏文件，请等待完成后再继续。");
    }

    private static string CaptureDirectoryStamp(string root)
    {
        try
        {
            using var sha = SHA256.Create();
            var files = EnumerateGameFiles(root).Select(path =>
            {
                var info = new FileInfo(path);
                return $"{Normalize(Path.GetRelativePath(root, path))}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
            }).OrderBy(value => value, StringComparer.OrdinalIgnoreCase);
            var bytes = Encoding.UTF8.GetBytes(string.Join("\n", files));
            return Convert.ToHexString(sha.ComputeHash(bytes));
        }
        catch { return Guid.NewGuid().ToString("N"); }
    }

    private static OverwatchRegionManifest Scan(string root, OverwatchRegion region,
        IProgress<RegionProgress>? progress, CancellationToken token)
    {
        root = Path.GetFullPath(root);
        var files = EnumerateGameFiles(root).ToList();
        var manifest = new OverwatchRegionManifest { Region = region };
        for (var i = 0; i < files.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            var file = files[i];
            progress?.Report(new RegionProgress($"正在记录当前{RegionName(region)}文件… {i + 1:N0} / {files.Count:N0}",
                i + 1, files.Count));
            var relative = Normalize(Path.GetRelativePath(root, file));
            manifest.Files[relative] = ReadStableEntry(file, relative, token);
        }
        manifest.BuildFingerprint = ReadBuildFingerprint(root, manifest.Files);
        return manifest;
    }

    private static RegionScanResult ScanBestEffort(string root, OverwatchRegion region,
        IProgress<RegionProgress>? progress, CancellationToken token)
    {
        root = Path.GetFullPath(root);
        var files = EnumerateGameFiles(root).ToList();
        var manifest = new OverwatchRegionManifest { Region = region };
        var issues = new List<RegionFileIssue>();
        for (var i = 0; i < files.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            var file = files[i];
            var relative = Normalize(Path.GetRelativePath(root, file));
            progress?.Report(new RegionProgress($"正在记录当前{RegionName(region)}文件… {i + 1:N0} / {files.Count:N0}",
                i + 1, files.Count));
            try
            {
                manifest.Files[relative] = ReadStableEntry(file, relative, token);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                issues.Add(new RegionFileIssue { RelativePath = relative, Reason = "文件读取或 Hash 失败：" + ex.Message });
            }
        }
        // VerifiedDifference 会立即去掉 build fingerprint；这里不让单个版本文件异常升级成整次扫描失败。
        try { manifest.BuildFingerprint = ReadBuildFingerprint(root, manifest.Files); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            issues.Add(new RegionFileIssue { RelativePath = ".build.info", Reason = "版本文件读取失败：" + ex.Message });
        }
        return new RegionScanResult(manifest, issues);
    }

    private static RegionFileEntry ReadStableEntry(string file, string relative, CancellationToken token)
    {
        for (var attempt = 0; attempt <= ScanRetryCount; attempt++)
        {
            token.ThrowIfCancellationRequested();
            var before = new FileInfo(file);
            var size = before.Length;
            var modified = before.LastWriteTimeUtc;
            var hash = ComputeHash(file, token);
            var after = new FileInfo(file);
            if (after.Exists && size == after.Length && modified == after.LastWriteTimeUtc)
                return new RegionFileEntry { RelativePath = relative, Size = size, LastWriteTimeUtc = modified, Sha256 = hash };
            if (attempt < ScanRetryCount) Thread.Sleep(250);
        }
        throw new IOException($"扫描期间文件仍在变化：{relative}。请等待 Battle.net 完成更新后重试。");
    }

    public static GameBuildFingerprint ReadBuildFingerprint(string root,
        IReadOnlyDictionary<string, RegionFileEntry>? scanned = null)
    {
        var exe = FindExecutable(root);
        var vi = exe is null ? null : FileVersionInfo.GetVersionInfo(exe);
        var build = FindFile(root, ".build.info");
        var core = scanned?.Values.Where(entry => IsCoreFile(entry.RelativePath))
            .OrderBy(entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Select(entry => $"{entry.RelativePath}|{entry.Size}|{entry.Sha256}") ?? Array.Empty<string>();
        return new GameBuildFingerprint
        {
            BuildInfoSha256 = build is null ? "" : ComputeHash(build),
            ExecutableFileVersion = vi?.FileVersion ?? "",
            ExecutableProductVersion = vi?.ProductVersion ?? "",
            ExecutableSize = exe is null ? 0 : new FileInfo(exe).Length,
            CoreFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", core)))),
        };
    }

    public static IEnumerable<string> EnumerateGameFiles(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Where(path => !IsIgnored(path, root));

    public static string? FindExecutable(string root)
    {
        foreach (var path in new[] { Path.Combine(root, "Overwatch.exe"), Path.Combine(root, "_retail_", "Overwatch.exe") })
            if (File.Exists(path)) return path;
        return null;
    }

    private static string? FindFile(string root, string name)
    {
        foreach (var path in new[] { Path.Combine(root, name), Path.Combine(root, "_retail_", name) })
            if (File.Exists(path)) return path;
        return null;
    }

    public static string ComputeHash(string path, CancellationToken token = default)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 1024, FileOptions.SequentialScan);
        using var sha = SHA256.Create();
        var buffer = new byte[1024 * 1024];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            token.ThrowIfCancellationRequested();
            sha.TransformBlock(buffer, 0, read, null, 0);
        }
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha.Hash!);
    }

    public static string Normalize(string relativePath) => relativePath.Replace('\\', '/').TrimStart('/');

    private static bool IsCoreFile(string relative) =>
        relative.Equals(".build.info", StringComparison.OrdinalIgnoreCase) ||
        relative.EndsWith("/Overwatch.exe", StringComparison.OrdinalIgnoreCase) ||
        relative.EndsWith("/Overwatch_loader.dll", StringComparison.OrdinalIgnoreCase);

    public static bool IsCommonBaselineFile(string relative) =>
        !relative.Equals(".build.info", StringComparison.OrdinalIgnoreCase) &&
        (relative.Equals("Overwatch.exe", StringComparison.OrdinalIgnoreCase) ||
         relative.EndsWith("/Overwatch.exe", StringComparison.OrdinalIgnoreCase) ||
         relative.EndsWith("/Overwatch_loader.dll", StringComparison.OrdinalIgnoreCase) ||
         relative.StartsWith("data/casc/config/", StringComparison.OrdinalIgnoreCase) ||
         relative.StartsWith("data/casc/indices/", StringComparison.OrdinalIgnoreCase));

    private static bool IsIgnored(string path, string root)
    {
        var relative = Normalize(Path.GetRelativePath(root, path));
        return IsIgnoredRelativePath(relative);
    }

    public static bool IsIgnoredRelativePath(string relativePath)
    {
        var relative = Normalize(relativePath);
        if (relative.StartsWith(".battle.net/", StringComparison.OrdinalIgnoreCase) ||
            relative.Contains("/.cloudlightblizzard-", StringComparison.OrdinalIgnoreCase))
            return true;

        if (IgnoredRuntimePrefixes.Any(prefix => relative.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            return true;

        var fileName = Path.GetFileName(relative);
        if (fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".temp", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".log", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".dmp", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".mdmp", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".etl", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".pid", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".lock", StringComparison.OrdinalIgnoreCase))
            return true;

        return relative.StartsWith("data/casc/", StringComparison.OrdinalIgnoreCase) &&
               (fileName.Equals("shmem", StringComparison.OrdinalIgnoreCase) ||
                fileName.StartsWith("shmem.", StringComparison.OrdinalIgnoreCase));
    }

    private static string RegionName(OverwatchRegion region) => region == OverwatchRegion.China ? "国服" : "国际服";
}
