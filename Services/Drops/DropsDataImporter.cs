using System.IO;
using System.Security.Cryptography;

namespace CloudLightBlizzard.Services.Drops;

public sealed class DropsDataImporter
{
    private static readonly IReadOnlyDictionary<DropsPlatform, ImportCandidate[]> Layouts =
        new Dictionary<DropsPlatform, ImportCandidate[]>
        {
            [DropsPlatform.Soop] =
            [
                new("settings.json"), new("accounts", true), new("cookies.json", true),
                new(".disclaimer_accepted"), new("logs")
            ],
            [DropsPlatform.YouTube] =
            [
                new("config.json"), new("profiles", true), new("logs"),
                new("watch_history.json")
            ],
            [DropsPlatform.Twitch] =
            [
                new("settings.json"), new("cookies.jar", true), new("cache"),
                new("log.txt"), new("logs")
            ],
            [DropsPlatform.Bilibili] =
            [
                new("state.json"), new("credential.dpapi", true),
                new("notifier.dpapi", true), new("logs")
            ],
        };

    public IReadOnlyList<string> Detect(DropsPlatform platform, string sourceDirectory)
    {
        var source = Path.GetFullPath(sourceDirectory);
        if (!Directory.Exists(source)) return Array.Empty<string>();
        return Layouts[platform]
            .Select(candidate => Path.Combine(source, candidate.RelativePath))
            .Where(path => File.Exists(path) || Directory.Exists(path))
            .Select(path => Path.GetRelativePath(source, path))
            .ToList();
    }

    public async Task<ImportResult> ImportAsync(
        DropsPlatform platform,
        string sourceDirectory,
        string destinationDirectory,
        Func<string, ImportConflictAction> onConflict,
        CancellationToken cancellationToken = default)
    {
        var sourceRoot = Path.GetFullPath(sourceDirectory).TrimEnd(Path.DirectorySeparatorChar);
        var destinationRoot = Path.GetFullPath(destinationDirectory).TrimEnd(Path.DirectorySeparatorChar);
        if (!Directory.Exists(sourceRoot))
            return new(false, false, Array.Empty<string>(), Array.Empty<string>(), ["所选目录不存在"]);
        if (PathsEqual(sourceRoot, destinationRoot))
            return new(false, false, Array.Empty<string>(), Array.Empty<string>(), ["源目录与目标目录相同"]);

        Directory.CreateDirectory(destinationRoot);
        var copied = new List<string>();
        var skipped = new List<string>();
        var failed = new List<string>();
        var candidates = Layouts[platform];
        var detected = Detect(platform, sourceRoot);
        if (detected.Count == 0)
            return new(false, false, copied, skipped, ["没有识别到该平台的旧版数据"]);

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = SafeCombine(sourceRoot, candidate.RelativePath);
            if (!File.Exists(source) && !Directory.Exists(source)) continue;
            var destination = SafeCombine(destinationRoot, candidate.RelativePath);
            var hasConflict = File.Exists(destination) ||
                              (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any());
            var overwrite = false;
            if (hasConflict)
            {
                var action = onConflict(candidate.RelativePath);
                if (action == ImportConflictAction.Cancel)
                    return new(false, true, copied, skipped, failed);
                if (action == ImportConflictAction.Skip)
                {
                    skipped.Add(candidate.RelativePath);
                    continue;
                }
                overwrite = true;
            }

            try
            {
                if (File.Exists(source))
                    await CopyFileVerifiedAsync(source, destination, overwrite, cancellationToken);
                else
                    await CopyDirectoryVerifiedAsync(source, destination, overwrite, cancellationToken);
                copied.Add(candidate.RelativePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed.Add($"{candidate.RelativePath}: {ex.Message}");
            }
        }
        return new(failed.Count == 0 && copied.Count > 0, false, copied, skipped, failed);
    }

    private static async Task CopyDirectoryVerifiedAsync(string source, string destination, bool overwrite,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            if (File.Exists(target) && !overwrite) continue;
            await CopyFileVerifiedAsync(file, target, overwrite, cancellationToken);
        }
    }

    private static async Task CopyFileVerifiedAsync(string source, string destination, bool overwrite,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + ".import-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read,
                             128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             128 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
                await input.CopyToAsync(output, cancellationToken);

            if (!await FilesMatchAsync(source, temporary, cancellationToken))
                throw new IOException("复制校验失败。");
            File.Move(temporary, destination, overwrite);
            if (!await FilesMatchAsync(source, destination, cancellationToken))
                throw new IOException("目标文件校验失败。");
        }
        finally
        {
            try { File.Delete(temporary); } catch { }
        }
    }

    private static async Task<bool> FilesMatchAsync(string left, string right, CancellationToken cancellationToken)
    {
        if (new FileInfo(left).Length != new FileInfo(right).Length) return false;
        await using var leftStream = File.OpenRead(left);
        await using var rightStream = File.OpenRead(right);
        var leftHash = await SHA256.HashDataAsync(leftStream, cancellationToken);
        var rightHash = await SHA256.HashDataAsync(rightStream, cancellationToken);
        return leftHash.AsSpan().SequenceEqual(rightHash);
    }

    private static string SafeCombine(string root, string relative)
    {
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("数据路径越界。");
        return path;
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(Path.GetFullPath(left).TrimEnd('\\', '/'), Path.GetFullPath(right).TrimEnd('\\', '/'),
            StringComparison.OrdinalIgnoreCase);
}
