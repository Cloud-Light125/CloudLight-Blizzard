using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace BnetSwitch.Services.Overwatch;

// --owtest 用:扫码 → 遍历本机账号库的每个 roleId,拉概览+详细并翻译,验证 C# 链路。
// 结果由 App 写进 owtest.txt。纯验证,不写登录态到磁盘。
public static class OwProbe
{
    // --matchtest:用缓存登录态实测 queryMatchList 翻页(验证对局列表不空)。写 matchtest.txt。
    public static async Task RunMatchTestAsync(StringBuilder sb)
    {
        var client = await DashenAuth.GetAliveAsync();
        if (client is null) { sb.AppendLine("无缓存登录态,先在 App 里扫码查一次再跑。"); return; }
        var own = await client.GetOwnRoleAsync();
        if (own is not { } o) { sb.AppendLine("拿不到自己的角色。"); return; }
        sb.AppendLine($"own roleId={o.RoleId}");
        var svc = new BnetSwitch.Stats.StatsService(client);
        foreach (var gm in new[] { "sport", "leisure", "fight", "lfight" })
        {
            var list = new List<BnetSwitch.Stats.MatchRecord>();
            try
            {
                int p1 = await svc.LoadMoreMatchesAsync(list, o.RoleId, gm, false, 1);
                int p2 = p1 >= 12 ? await svc.LoadMoreMatchesAsync(list, o.RoleId, gm, false, 2) : 0;
                sb.AppendLine($"[{gm}] 第1页={p1} 第2页={p2} 累计={list.Count}" +
                              (list.Count > 0 ? $"  首条:{list[0].Result} {list[0].ScoreText} {list[0].Map} {list[0].Hero} {list[0].Kda}" : "  (空)"));
            }
            catch (Exception ex) { sb.AppendLine($"[{gm}] 出错:{ex.Message}"); }
        }
    }

    // --friendtest:验证 roleId 是否驱动好友列表(能不能查别人好友)。写 friendtest.txt。
    public static async Task RunFriendTestAsync(StringBuilder sb)
    {
        var client = await DashenAuth.GetAliveAsync();
        if (client is null) { sb.AppendLine("无缓存登录态,先在 App 里扫码查一次。"); return; }
        var own = await client.GetOwnRoleAsync();
        long rid = own?.RoleId ?? 100001;
        var svc = new BnetSwitch.Stats.StatsService(client);
        var fd = await svc.LoadFriendsAsync(rid, 23);
        sb.AppendLine($"聚合各模式后:{fd.Rows.Count} 个有OW数据的好友");
        foreach (var r in fd.Rows) sb.AppendLine($"   {r.Name} | {r.TierText} | {r.StatText}");
    }

    public static async Task RunAsync(StringBuilder sb)
    {
        DashenClient client;
        var cached = await DashenAuth.GetAliveAsync();
        if (cached != null)
        {
            client = cached;
            sb.AppendLine("复用已缓存登录态,免扫码。");
        }
        else
        {
            client = new DashenClient();
            sb.AppendLine("生成二维码…");
            var qr = await client.CreateLoginQrAsync();

            string owDir = AppPaths.Current.OverwatchDataDir;
            Directory.CreateDirectory(owDir);
            string qrPath = Path.Combine(owDir, "owtest_qr.png");
            await File.WriteAllBytesAsync(qrPath, qr.ImageBytes);
            Open(qrPath);
            sb.AppendLine($"二维码已打开: {qrPath}");
            sb.AppendLine(">>> 请用【网易大神App】扫码登录 <<<");

            var pr = new DashenClient.PollResult(DashenClient.ScanState.Waiting);
            for (int i = 0; i < 55; i++)   // ~110s
            {
                await Task.Delay(2000);
                pr = await client.PollOnceAsync();
                if (pr.State == DashenClient.ScanState.Confirmed) break;
                if (pr.State == DashenClient.ScanState.Expired) { sb.AppendLine("二维码过期/被拒。"); return; }
            }
            if (pr.State != DashenClient.ScanState.Confirmed) { sb.AppendLine("超时未扫码。"); return; }

            await client.CompleteLoginAsync();
            DashenAuth.Save(client);
            sb.AppendLine($"登录成功: {pr.UserName}");
        }
        sb.AppendLine();

        var maps = new OwMappings();
        await maps.InitAsync();
        sb.AppendLine($"映射表: 英雄 {maps.HeroCount}，地图 {maps.MapCount}");

        // 当前赛季:用扫码号探
        string season = "23";
        var own = await client.GetOwnRoleAsync();
        if (own is { } o)
        {
            var tk = await client.GetReportTokenAsync(o.RoleId, o.Server);
            foreach (var s in new[] { "23", "24", "25", "22", "21", "20", "18" })
                if (HasDetail(Parse(await client.QueryCountInfoRawAsync(o.RoleId, tk, s, "sport", o.Server)))) { season = s; break; }
        }
        sb.AppendLine($"当前赛季 season={season}");
        sb.AppendLine(new string('=', 62));

        foreach (var roleId in AccountRoleIds())
        {
            sb.AppendLine();
            try
            {
                var token = await client.GetReportTokenAsync(roleId);
                var card = Parse(await client.QueryCardRawAsync(roleId, token, "1"));
                sb.AppendLine($"◆ roleId={roleId}  {FindStr(card, "name")}  称号[{FindStr(card, "title")}] " +
                              $"等级{FindLong(card, "level")} 时长{FindStr(card, "gameTime")}h");
                sb.AppendLine($"   头像图: {FindStr(card, "icon")}");

                var d = Parse(await client.QueryCountInfoRawAsync(roleId, token, season, "sport"));
                if (!HasDetail(d)) { sb.AppendLine("   (本赛季无详细数据)"); continue; }

                foreach (var g in FindArr(d, "guideCountData"))
                {
                    var last = GetObj(g, "lastRankInfo");
                    var (cn, brush) = OwMappings.Rank(GetStr(last, "rank_name"));
                    sb.AppendLine($"   段位 {GetStr(g, "roleType"),-7} {cn} {GetLong(last, "rank_sub_tier")} " +
                                  $"分{GetLong(last, "rankScore")}  场{GetLong(g, "matchSum")} 胜率{GetStr(g, "winRate")}  [{brush ?? "-"}]");
                }

                if (Find(d, "presetsSummaryData") is { } pv)
                    sb.AppendLine($"   场均 伤害{GetStr(pv, "aveHeroDamage")} 治疗{GetStr(pv, "aveCure")} " +
                                  $"胜率{GetStr(pv, "winRate")} 场次{GetLong(pv, "matchSum")}");

                foreach (var h in FindArr(d, "presetsHeroUseSummaryList")
                             .OrderByDescending(x => GetLong(x, "matchSum")).Take(5))
                {
                    var hg = GetStr(h, "heroGuid");
                    sb.AppendLine($"   英雄 {maps.HeroName(hg),-8} 场{GetLong(h, "matchSum")} 胜率{GetStr(h, "winRate")}  立绘:{maps.HeroIcon(hg)}");
                }

                foreach (var m in FindArr(d, "matchList").Take(5))
                {
                    var mg = GetStr(m, "mapGuid"); var hg = GetStr(m, "heroGuid");
                    string ret = GetLong(m, "matchRet") == 1 ? "胜" : "负";
                    sb.AppendLine($"   对局 {ret} {maps.HeroName(hg),-6} 图[{maps.MapName(mg)}] mg={mg} " +
                                  $"{GetLong(m, "kill")}/{GetLong(m, "death")}/{GetLong(m, "assist")}");
                }
            }
            catch (Exception ex) { sb.AppendLine($"   查询失败: {ex.Message}"); }
        }

        sb.AppendLine();
        sb.AppendLine("== searchBnetAccount 入参试探（匿名示例）==");
        string tag = "Player#1234", nm = "Player";
        if (own is { } osr)
        {
            string stok = "";
            try { stok = await client.GetReportTokenAsync(osr.RoleId, osr.Server); } catch { }
            const string BASE = "https://datamsapi.ds.163.com/v1/a19ld5tool/searchBnetAccount";
            string common = $"roleId={osr.RoleId}&token={stok}&server=1&dts=2026";
            string kw = Uri.EscapeDataString(tag), kwn = Uri.EscapeDataString(nm);
            var cases = new (string url, object body)[]
            {
                ($"{BASE}?{common}", new { keyword = tag }),
                ($"{BASE}?{common}", new { bnetName = nm, bnetId = "1234" }),
                ($"{BASE}?{common}", new { name = tag }),
                ($"{BASE}?{common}&keyword={kw}", new { }),
                ($"{BASE}?{common}&bnetName={kwn}&bnetId=1234", new { }),
                ($"{BASE}?{common}&searchName={kwn}", new { }),
                ($"{BASE}?token={stok}&server=1&dts=2026&keyword={kw}", new { }),
            };
            foreach (var (url, body) in cases)
            {
                try
                {
                    var r = await client.AuthedPostRawAsync(url, body);
                    sb.AppendLine($"  URL:{url[url.IndexOf('?')..]}  BODY:{JsonSerializer.Serialize(body)}");
                    sb.AppendLine($"     -> {r[..Math.Min(240, r.Length)]}");
                }
                catch (Exception ex) { sb.AppendLine($"  ERR {ex.Message}"); }
            }
        }

        sb.AppendLine();
        sb.AppendLine("预取英雄/地图缩略图到本地缓存(下一次开战绩走本地)…");
        int heroImgs = await OwImageCache.PrefetchAsync(maps.AllHeroIconUrls(), thumbWidth: 128);
        int mapImgs = await OwImageCache.PrefetchAsync(maps.AllMapIconUrls(), thumbWidth: 160);
        sb.AppendLine($"已缓存 英雄 {heroImgs} + 地图 {mapImgs} 张缩略图 → {OwImageCache.CacheRoot}");

        sb.AppendLine();
        sb.AppendLine("===== 若上面段位/场均/英雄/对局/图片URL/地图名 都正常，C# 链路即验证通过 =====");
    }

    // ---- 账号 roleId：账号库文件夹名就是 roleId ----
    private static IEnumerable<long> AccountRoleIds()
    {
        string dir = AppPaths.Current.AccountsDir;
        if (!Directory.Exists(dir)) yield break;
        foreach (var d in Directory.GetDirectories(dir))
            if (long.TryParse(Path.GetFileName(d), out var id))
                yield return id;
    }

    // ---- JSON 小工具 ----
    private static JsonElement Parse(string s) { using var doc = JsonDocument.Parse(s); return doc.RootElement.Clone(); }

    private static JsonElement? Find(JsonElement? el, string key)
    {
        if (el is not { } e) return null;
        if (e.ValueKind == JsonValueKind.Object)
        {
            if (e.TryGetProperty(key, out var v)) return v;
            foreach (var p in e.EnumerateObject()) { var r = Find(p.Value, key); if (r != null) return r; }
        }
        else if (e.ValueKind == JsonValueKind.Array)
            foreach (var it in e.EnumerateArray()) { var r = Find(it, key); if (r != null) return r; }
        return null;
    }

    private static IEnumerable<JsonElement> FindArr(JsonElement? el, string key)
        => Find(el, key) is { ValueKind: JsonValueKind.Array } a ? a.EnumerateArray() : Enumerable.Empty<JsonElement>();

    private static string FindStr(JsonElement? el, string key) => AsStr(Find(el, key));
    private static long FindLong(JsonElement? el, string key) => AsLong(Find(el, key));

    private static JsonElement GetObj(JsonElement el, string prop)
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) ? v : default;

    private static string GetStr(JsonElement el, string prop)
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) ? AsStr(v) : "";
    private static long GetLong(JsonElement el, string prop)
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) ? AsLong(v) : 0;

    private static string AsStr(JsonElement? e)
        => e is not { } v ? "" : v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" :
           v.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False ? v.ToString() : "";
    private static long AsLong(JsonElement? e)
        => e is { ValueKind: JsonValueKind.Number } v && v.TryGetInt64(out var n) ? n : 0;

    private static bool HasDetail(JsonElement? d)
        => FindArr(d, "guideCountData").Any() || FindArr(d, "matchList").Any();

    private static void Open(string path)
    {
        try { Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true }); } catch { }
    }
}
