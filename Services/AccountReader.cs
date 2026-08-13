using System.IO;
using System.Text.Json;
using CloudLightBlizzard.Models;
using Microsoft.Data.Sqlite;

namespace CloudLightBlizzard.Services;

/// <summary>
/// 从战网 CachedData.db 读取账号列表,以及「当前登录的是哪个账号」。
/// </summary>
public sealed class AccountReader
{
    private readonly BattleNetPaths _paths;

    public AccountReader(BattleNetPaths paths) => _paths = paths;

    /// <summary>
    /// 读取所有账号。<paramref name="activeAccountId"/> 返回当前活跃账号的 id(读不到则为 null)。
    /// </summary>
    public IReadOnlyList<BattleAccount> ReadAccounts(out long? activeAccountId)
    {
        activeAccountId = null;
        var list = new List<BattleAccount>();
        if (!File.Exists(_paths.CachedDataDb)) return list;

        // 复制一份再读,避免战网正在运行时的文件锁 / WAL 状态问题。
        var tmp = Path.Combine(Path.GetTempPath(), $"bam_cacheddata_{Guid.NewGuid():N}.db");
        CopyDbWithSidecars(_paths.CachedDataDb, tmp);
        try
        {
            var cs = new SqliteConnectionStringBuilder
            {
                DataSource = tmp,
                Mode = SqliteOpenMode.ReadOnly,
            }.ToString();

            using var conn = new SqliteConnection(cs);
            conn.Open();

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    "SELECT name, environment, battle_tag, account_id_lo FROM login_cache";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    list.Add(new BattleAccount
                    {
                        InternalName = r.IsDBNull(0) ? "" : r.GetString(0),
                        Environment = r.IsDBNull(1) ? "" : r.GetString(1),
                        BattleTag = r.IsDBNull(2) ? "" : r.GetString(2),
                        AccountId = r.IsDBNull(3) ? 0 : r.GetInt64(3),
                    });
                }
            }

            activeAccountId = ReadActiveAccountId(conn);
        }
        finally
        {
            TryDeleteWithSidecars(tmp);
        }

        return list;
    }

    /// <summary>
    /// 只读取「当前登录账号」的 id。保存 / 切换前实时调用,避免用界面上缓存的旧值。
    /// </summary>
    public long? ReadActiveAccountId()
    {
        if (!File.Exists(_paths.CachedDataDb)) return null;

        var tmp = Path.Combine(Path.GetTempPath(), $"bam_active_{Guid.NewGuid():N}.db");
        CopyDbWithSidecars(_paths.CachedDataDb, tmp);
        try
        {
            var cs = new SqliteConnectionStringBuilder
            {
                DataSource = tmp,
                Mode = SqliteOpenMode.ReadOnly,
            }.ToString();
            using var conn = new SqliteConnection(cs);
            conn.Open();
            return ReadActiveAccountId(conn);
        }
        catch
        {
            return null;
        }
        finally
        {
            TryDeleteWithSidecars(tmp);
        }
    }

    /// <summary>
    /// 当前账号指针存在 key_value_store 的 features_cached_data_points 里:
    /// {"account_id":100001,...}
    /// </summary>
    private static long? ReadActiveAccountId(SqliteConnection conn)
    {
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "SELECT value FROM key_value_store WHERE key = 'features_cached_data_points'";
            if (cmd.ExecuteScalar() is not string val || string.IsNullOrEmpty(val)) return null;

            using var doc = JsonDocument.Parse(val);
            if (doc.RootElement.TryGetProperty("account_id", out var idEl) &&
                idEl.TryGetInt64(out var id))
                return id;
        }
        catch
        {
            // 表或键不存在时忽略,退回到「未知」。
        }
        return null;
    }

    private static void CopyDbWithSidecars(string src, string dst)
    {
        File.Copy(src, dst, overwrite: true);
        foreach (var ext in new[] { "-wal", "-shm" })
            if (File.Exists(src + ext))
                File.Copy(src + ext, dst + ext, overwrite: true);
    }

    private static void TryDeleteWithSidecars(string path)
    {
        foreach (var p in new[] { path, path + "-wal", path + "-shm" })
        {
            try { if (File.Exists(p)) File.Delete(p); }
            catch { /* 临时文件,删不掉也无妨 */ }
        }
    }
}
