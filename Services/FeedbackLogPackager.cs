using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using CloudLightBlizzard.Models;
using CloudLightBlizzard.Services.Drops;

namespace CloudLightBlizzard.Services;

public sealed class FeedbackLogPackager
{
    private const long MaxRawBytes = 40L * 1024 * 1024;
    private const long MaxPerFileBytes = 8L * 1024 * 1024;
    private static readonly HashSet<string> AllowedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "app.log", "cloudlight-blizzard.log", "account-switch.log", "update.log", "region-switch.log",
        "region-diff-diagnostic.txt", "crash.log", "exception.log", "drops-soop.log", "drops-youtube.log",
        "drops-twitch.log",
    };

    private readonly string _logsDirectory;
    public FeedbackLogPackager(string? logsDirectory = null) => _logsDirectory = logsDirectory ?? AppPaths.Current.LogsDir;

    public IReadOnlyList<FeedbackLogPreview> Preview()
    {
        if (!Directory.Exists(_logsDirectory)) return Array.Empty<FeedbackLogPreview>();
        var remaining = MaxRawBytes;
        var result = new List<FeedbackLogPreview>();
        IEnumerable<FileInfo> candidates;
        try
        {
            candidates = new DirectoryInfo(_logsDirectory).EnumerateFiles("*", SearchOption.TopDirectoryOnly)
                .Where(file => AllowedNames.Contains(file.Name))
                .OrderByDescending(file => file.LastWriteTimeUtc).ThenBy(file => file.Name).ToArray();
        }
        catch { return result; }

        foreach (var file in candidates)
        {
            if (remaining <= 0 || file.Length <= 0) continue;
            var included = Math.Min(Math.Min(file.Length, MaxPerFileBytes), remaining);
            result.Add(new FeedbackLogPreview(file.FullName, file.Name, included));
            remaining -= included;
        }
        return result;
    }

    public async Task<FeedbackPackage> CreateAsync(string category, CancellationToken cancellationToken = default)
    {
        var logs = Preview();
        var tempDirectory = Path.Combine(Path.GetTempPath(), "CloudLightBlizzard", "feedback");
        Directory.CreateDirectory(tempDirectory);
        var path = Path.Combine(tempDirectory, $"feedback-{Guid.NewGuid():N}.zip");
        try
        {
            await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
                64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            {
                var metadata = zip.CreateEntry("metadata.json", CompressionLevel.Fastest);
                await using (var metadataStream = metadata.Open())
                {
                    await JsonSerializer.SerializeAsync(metadataStream, new
                    {
                        appVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "unknown",
                        osVersion = Environment.OSVersion.VersionString,
                        architecture = RuntimeInformation.OSArchitecture.ToString(),
                        submittedAt = DateTimeOffset.Now,
                        category,
                    }, cancellationToken: cancellationToken);
                }

                foreach (var log in logs)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var entry = zip.CreateEntry(log.ArchiveName, CompressionLevel.Fastest);
                    await using var destination = entry.Open();
                    await WriteRedactedTailAsync(log, destination, cancellationToken);
                }
            }
            await output.FlushAsync(cancellationToken);
            var length = output.Length;
            if (length > CloudServiceConfiguration.MaximumZipBytes)
                throw new FeedbackPackageTooLargeException(length);
            return new FeedbackPackage(path, logs, length);
        }
        catch
        {
            try { File.Delete(path); } catch { }
            throw;
        }
    }

    private static async Task WriteRedactedTailAsync(FeedbackLogPreview log, Stream destination,
        CancellationToken cancellationToken)
    {
        await using var source = new FileStream(log.SourcePath, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var startsMidFile = source.Length > log.IncludedBytes;
        if (startsMidFile) source.Seek(-log.IncludedBytes, SeekOrigin.End);
        using var reader = new StreamReader(source, Encoding.UTF8, true, 64 * 1024, leaveOpen: true);
        if (startsMidFile) _ = await reader.ReadLineAsync(cancellationToken);
        await using var writer = new StreamWriter(destination, new UTF8Encoding(false), 64 * 1024, leaveOpen: true);
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            await writer.WriteLineAsync(SensitiveDataRedactor.Redact(line).AsMemory(), cancellationToken);
        }
        await writer.FlushAsync(cancellationToken);
    }
}

public sealed class FeedbackPackageTooLargeException(long length)
    : IOException($"Feedback log package is too large: {length} bytes")
{
    public long Length { get; } = length;
}
