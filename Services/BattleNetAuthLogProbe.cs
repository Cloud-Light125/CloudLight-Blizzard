using System.IO;
using System.Text.RegularExpressions;

namespace BnetSwitch.Services;

public enum BattleNetLoginEvidence { None, LoginPage, RealAuthExpired }
public sealed record BattleNetAuthLogCursor(IReadOnlyDictionary<string, long> Lengths);

/// <summary>只读查看切换开始后的 Battle.net 日志；不保存日志内容，也不匹配或提取认证值。</summary>
public sealed class BattleNetAuthLogProbe
{
    private readonly BattleNetPaths _paths;
    private static readonly Regex RealExpiry = new(
        @"ERROR_NO_COOKIE|session\s+(?:was\s+)?(?:revoked|invalidated|expired)|authentication\s+(?:session\s+)?(?:expired|invalid)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LoginPage = new(
        @"(?:show|open|navigate).{0,40}(?:login|logon)|(?:login|logon).{0,40}(?:page|screen)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public BattleNetAuthLogProbe(BattleNetPaths paths) => _paths = paths;

    public BattleNetAuthLogCursor CaptureCursor()
    {
        var root = Path.Combine(_paths.LocalRoot, "Logs");
        if (!Directory.Exists(root)) return new(new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase));
        try
        {
            return new(Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Select(path => new FileInfo(path))
                .Where(info => info.Length <= 8 * 1024 * 1024)
                .ToDictionary(info => info.FullName, info => info.Length, StringComparer.OrdinalIgnoreCase));
        }
        catch { return new(new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)); }
    }

    public BattleNetLoginEvidence ReadAppended(BattleNetAuthLogCursor cursor)
    {
        var root = Path.Combine(_paths.LocalRoot, "Logs");
        if (!Directory.Exists(root)) return BattleNetLoginEvidence.None;
        try
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                         .Select(path => new FileInfo(path)).Where(info => info.Length <= 8 * 1024 * 1024)
                         .OrderByDescending(info => info.LastWriteTimeUtc).Take(12))
            {
                string text;
                var previousLength = cursor.Lengths.TryGetValue(file.FullName, out var length) && file.Length >= length
                    ? length : 0;
                try { text = ReadAppended(file.FullName, previousLength, 256 * 1024); } catch { continue; }
                if (RealExpiry.IsMatch(text)) return BattleNetLoginEvidence.RealAuthExpired;
                if (LoginPage.IsMatch(text)) return BattleNetLoginEvidence.LoginPage;
            }
        }
        catch { }
        return BattleNetLoginEvidence.None;
    }

    private static string ReadAppended(string file, long previousLength, int maxBytes)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var start = Math.Max(previousLength, stream.Length - maxBytes);
        if (start >= stream.Length) return "";
        stream.Seek(start, SeekOrigin.Begin);
        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}
