namespace CloudLightBlizzard.Models;

/// <summary>
/// 从战网 CachedData.db 的 login_cache 表读出的一个账号。
/// </summary>
public sealed class BattleAccount
{
    /// <summary>account_id_lo,同时也是 Account\ 与 BrowserCaches\ 下的目录名。</summary>
    public long AccountId { get; init; }

    /// <summary>展示用的 BattleTag（UTF-8），例如「Player#1234」。</summary>
    public string BattleTag { get; init; } = "";

    /// <summary>登录环境,例如 cn.actual.battlenet.com.cn。</summary>
    public string Environment { get; init; } = "";

    /// <summary>login_cache.name,16 位十六进制的内部标识(非邮箱)。</summary>
    public string InternalName { get; init; } = "";
}
