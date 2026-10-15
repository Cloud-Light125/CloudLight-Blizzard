using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace CloudLightBlizzard.Services;

/// <summary>Copy verified profiles as a unit. Never merge or overwrite account directories.</summary>
internal static class AccountProfileMigration
{
    internal static IReadOnlyList<string> Run(string sourceRoot, string destinationRoot, List<string> migrated,
        Action<string>? log = null)
    {
        var warnings = new List<string>();
        try
        {
            AppDataStore.EnsureRecoveryRootEmpty(sourceRoot + ".recovery");
            AppDataStore.EnsureRecoveryRootEmpty(destinationRoot + ".recovery");
            if (!Directory.Exists(sourceRoot)) return warnings;
            foreach (var source in Directory.EnumerateDirectories(sourceRoot))
            {
                var name = Path.GetFileName(source);
                if (!long.TryParse(name, out var id) || id <= 0 || name != id.ToString(CultureInfo.InvariantCulture)) continue;
                var destination = Path.Combine(destinationRoot, name);
                // Even an incomplete existing directory belongs to the user. Keep both for diagnosis.
                if (Directory.Exists(destination) || File.Exists(destination))
                {
                    try { _ = AppDataStore.ValidateSnapshotDirectory(destination, id); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
                    { warnings.Add($"新目录已有不可用快照，旧数据保留，需人工核对：{source} → {destination}（{ex.GetType().Name}）"); }
                    continue;
                }
                var staging = destination + ".staging-migration-" + Guid.NewGuid().ToString("N");
                try
                {
                    var (_, sourceManifest, _) = AppDataStore.ValidateSnapshotDirectory(source, id);
                    var before = Fingerprint(source, sourceManifest);
                    Directory.CreateDirectory(staging);
                    foreach (var file in before.Files)
                    {
                        var target = Path.Combine(staging, file);
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        File.Copy(Path.Combine(source, file), target, overwrite: false);
                    }
                    var (_, copiedManifest, _) = AppDataStore.ValidateSnapshotDirectory(staging, id);
                    var copied = Fingerprint(staging, copiedManifest);
                    var (_, currentManifest, _) = AppDataStore.ValidateSnapshotDirectory(source, id);
                    var after = Fingerprint(source, currentManifest);
                    if (before.Digest != copied.Digest || before.Digest != after.Digest)
                        throw new InvalidDataException("迁移期间源快照变化或复制校验失败。");
                    Directory.Move(staging, destination);
                    migrated.Add("accounts/" + name);
                    log?.Invoke($"AccountMigration files={before.Files.Count};sha256={before.Digest};source={source};destination={destination}");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
                { warnings.Add($"账号快照迁移未完成，原数据保留：{source} → {destination}（{ex.GetType().Name}）"); }
                finally
                {
                    try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch { }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        { warnings.Add("账号快照迁移不可用：" + ex.Message); }
        foreach (var warning in warnings) log?.Invoke(warning);
        return warnings;
    }

    private static (List<string> Files, string Digest) Fingerprint(string root, BattleNetAccountSnapshotManifest manifest)
    {
        // Only verified, managed files are owned by migration. Never read/copy
        // undeclared files (including protected authentication leftovers).
        var files = manifest.Entries.Where(entry => !entry.IsDirectory)
            .Select(entry => Path.Combine("BattleNet", entry.RelativePath.Replace('/', Path.DirectorySeparatorChar)))
            .Append("meta.json").ToList();
        if (File.Exists(Path.Combine(root, "manifest.json"))) files.Add("manifest.json");
        files.Sort(StringComparer.Ordinal);
        using var aggregate = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var relative in files)
        {
            using var stream = File.OpenRead(Path.Combine(root, relative));
            aggregate.AppendData(Encoding.UTF8.GetBytes(relative + "\0"));
            aggregate.AppendData(SHA256.HashData(stream));
        }
        return (files, Convert.ToHexString(aggregate.GetHashAndReset()));
    }
}
