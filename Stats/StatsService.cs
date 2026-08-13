using System.Text.Json;
using System.Windows.Media;
using BnetSwitch.Services.Overwatch;

namespace BnetSwitch.Stats;

// 把大神 API 数据映射成界面用的 PlayerStats:段位/场均/英雄/对局 + 中文翻译 + 图片本地缓存路径。
public sealed class StatsService
{
    private readonly DashenClient _client;
    private readonly OwMappings _maps = new();
    private bool _ready;
    private string? _ownBigdata;   // 自己的 bigdata token(customer/* 的 GL-Bigdata-Auth-Token 头 + 搜索授权)
    private long _ownRoleId;        // 自己的 roleId(GL-Bigdata-Role-Id + 搜索授权)
    private string? _sign;          // 自己 customerToken 里的 sign(给任意 bnetId 构造 customerToken)

    public StatsService(DashenClient client) => _client = client;

    private async Task EnsureReadyAsync()
    {
        if (_ready) return;
        await _maps.InitAsync();
        var own = await _client.GetOwnRoleAsync() ?? throw new InvalidOperationException("拿不到自己绑定的守望先锋角色");
        _ownRoleId = own.RoleId;
        _ownBigdata = await _client.GetReportTokenAsync(own.RoleId, own.Server);
        // 自己 queryCard 的 customerToken 里解出 sign,之后给任意 bnetId 构造 customerToken
        var ownCard = Parse(await _client.QueryCardRawAsync(own.RoleId, _ownBigdata, "1"));
        _sign = DashenClient.ExtractSign(FindStr(ownCard, "customerToken"));
        // 后台把全部英雄/地图缩略图预取到本地(不阻塞)
        _ = OwImageCache.PrefetchAsync(_maps.AllHeroIconUrls(), 128);
        _ = OwImageCache.PrefetchAsync(_maps.AllMapIconUrls(), 160);
        _ready = true;
    }

    /// <summary>查别人:BattleTag → bnetId(searchBnetAccount,body 传 token/roleId/name)。查不到返回 null。</summary>
    public async Task<long?> ResolveBattleTagAsync(string battleTag)
    {
        await EnsureReadyAsync();
        try
        {
            var d = Parse(await _client.SearchBnetAccountRawAsync(_ownBigdata!, _ownRoleId, battleTag.Trim()));
            long id = FindLong(d, "bnetId");
            return id == 0 ? null : id;
        }
        catch { return null; }
    }

    /// <summary>按 bnetId(=roleId)拉战绩。season 留空=当前;gameMode=sport 竞技/leisure 休闲/fight 角斗领域/lfight 休闲角斗;useOpen=开放职责否则预设。</summary>
    public async Task<PlayerStats> LoadAsync(long roleId, Func<string, Brush?> findBrush, string season = "", string gameMode = "sport", bool useOpen = false)
    {
        await EnsureReadyAsync();
        var ct = DashenClient.BuildCustomerToken(_sign!, roleId);
        var card = Parse(await _client.CustomerGetRawAsync("queryCard", ct, _ownBigdata!, _ownRoleId, ("season", "")));
        if (gameMode is "fight" or "lfight")   // 角斗领域 / 休闲角斗:走 customer/fight/*
        {
            bool leisure = gameMode == "lfight";
            string ep = leisure ? "customer/fight/getLeisureFightRoleCard" : "customer/fight/queryCount";
            var df = Parse(await _client.CustomerGetPathRawAsync(ep, ct, _ownBigdata!, _ownRoleId));
            return await BuildFightAsync(card, df, findBrush, leisure);
        }
        var d = Parse(await _client.CustomerGetRawAsync("queryCountInfo", ct, _ownBigdata!, _ownRoleId, ("gameMode", gameMode), ("season", season)));
        return await BuildAsync(card, d, findBrush, gameMode, useOpen);
    }

    /// <summary>翻页拉对局(每页12)。竞技/休闲=customer/queryMatchList(gameMode=sport/leisure);角斗=customer/fight/queryMatchList(gameMode=SportFight/LeisureFight)。返回本页条数(&lt;12=到底)。</summary>
    public async Task<int> LoadMoreMatchesAsync(ICollection<MatchRecord> into, long roleId, string gameMode, bool useOpen, int page)
    {
        await EnsureReadyAsync();
        var ct = DashenClient.BuildCustomerToken(_sign!, roleId);
        bool isFight = gameMode is "fight" or "lfight";
        string endpoint = isFight ? "fight/queryMatchList" : "queryMatchList";
        string queue = isFight ? (gameMode == "lfight" ? "LeisureFight" : "SportFight")
                               : (gameMode == "leisure" ? "leisure" : "sport");
        string? label = isFight ? (gameMode == "lfight" ? "休闲角斗" : "竞技角斗") : null;   // 竞技用每条自带 gameMode
        var d = Parse(await _client.CustomerGetRawAsync(endpoint, ct, _ownBigdata!, _ownRoleId,
            ("gameMode", queue), ("page", page.ToString())));
        int count = 0;
        foreach (var m in UnwrapArray(d)) { await AddMatchAsync(into, m, label, isFight); count++; }
        return count;
    }

    // queryMatchList 的 data 直接是数组(裹在 {code,data:[…]} 里)
    private static IEnumerable<JsonElement> UnwrapArray(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array) return root.EnumerateArray();
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Array)
            return d.EnumerateArray();
        return Enumerable.Empty<JsonElement>();
    }

    /// <summary>拉单局记分板(queryMatchInfo)。roleId=这局所属玩家,matchId=对局号。isFight=角斗(结构不同)。</summary>
    public async Task<MatchDetail?> LoadMatchDetailAsync(long roleId, string matchId, bool isFight = false)
    {
        await EnsureReadyAsync();
        if (string.IsNullOrEmpty(matchId)) return null;
        var ct = DashenClient.BuildCustomerToken(_sign!, roleId);
        if (isFight) return await LoadFightMatchDetailAsync(ct, roleId, matchId);
        var d = Parse(await _client.CustomerGetRawAsync("queryMatchInfo", ct, _ownBigdata!, _ownRoleId, ("matchId", matchId)));
        // 顶层字段裹在 data 里,走递归查找(直接取属性会落空)
        long ret = FindLong(d, "matchRet");
        var mg = FindStr(d, "mapGuid");
        long secs = FindLong(d, "gameTimeSec");
        long start = FindLong(d, "startTime");
        var md = new MatchDetail
        {
            MapName = _maps.MapName(mg),
            MapIconLocal = await OwImageCache.GetAsync(_maps.MapIcon(mg), 240),
            ModeText = _maps.MapMode(mg),
            ScoreText = $"{FindLong(d, "teamScore")} : {FindLong(d, "opponentScore")}",
            IsWin = ret == 1,
            ResultText = ret == 1 ? "胜利" : ret == -1 ? "失败" : "平局",
            Duration = Sec2Clock(secs),
            StartText = start > 0 ? DateTimeOffset.FromUnixTimeSeconds(start).LocalDateTime.ToString("MM-dd HH:mm") : "",
            EndText = start > 0 ? DateTimeOffset.FromUnixTimeSeconds(start + secs).LocalDateTime.ToString("HH:mm") : "",
        };

        // 禁用英雄(双方)
        foreach (var b in FindArr(d, "teamBanHeroGuids"))
        { var ic = await OwImageCache.GetAsync(_maps.HeroIcon(AsStr(b)), 128); if (ic != null) md.TeamBanIcons.Add(ic); }
        foreach (var b in FindArr(d, "enemyBanHeroGuids"))
        { var ic = await OwImageCache.GetAsync(_maps.HeroIcon(AsStr(b)), 128); if (ic != null) md.EnemyBanIcons.Add(ic); }

        // 被查玩家本局英雄面板(出战占比 + 独立数据)
        foreach (var h in FindArr(d, "heroList"))
        {
            var hg = GetStr(h, "heroId");
            md.MyHeroes.Add(new MatchHeroUse
            {
                HeroName = _maps.HeroName(hg),
                HeroIconLocal = await OwImageCache.GetAsync(_maps.HeroIcon(hg), 128),
                UseRateText = FormatPct(GetStr(h, "useTimeRate")),
                TimeText = Sec2Clock(GetLong(h, "userTimeSec")),
                Stats = BuildStatsFromMap(GetObj(h, "statMap")),
            });
        }

        // 先并发预取 perk 天赋图标(去重),避免逐个下载
        var perkUrls = new HashSet<string>();
        foreach (var p in FindArr(d, "teammateList").Concat(FindArr(d, "enemyList")))
            foreach (var pk in FindArr(p, "perks"))
            {
                var ic = _maps.Perk(GetStr(pk, "guid"))?.Icon;
                if (!string.IsNullOrEmpty(ic)) perkUrls.Add(ic);
            }
        await OwImageCache.PrefetchAsync(perkUrls, 128, 16);

        foreach (var p in FindArr(d, "teammateList")) md.Teammates.Add(await BuildPlayerAsync(p, roleId));
        foreach (var p in FindArr(d, "enemyList")) md.Enemies.Add(await BuildPlayerAsync(p, roleId));
        AssignGroups(FindArr(d, "teammateList").ToList(), md.Teammates);   // 组队判定
        AssignGroups(FindArr(d, "enemyList").ToList(), md.Enemies);
        return md;
    }

    private async Task<MatchPlayer> BuildPlayerAsync(JsonElement p, long meRole)
    {
        var (disp, _) = SplitTag(GetStr(p, "name"));
        var ri = GetObj(p, "rankInfo");
        var (cn, brushKey) = OwMappings.Rank(GetStr(ri, "rank_name"));
        long id = GetLong(p, "bnetId");
        var mp = new MatchPlayer
        {
            BnetId = id,
            IsMe = id == meRole,
            Name = disp,
            HeroIconLocal = await OwImageCache.GetAsync(GetStr(p, "heroIcon"), 128),
            Kad = $"{GetLong(p, "kill")}/{GetLong(p, "assist")}/{GetLong(p, "death")}",
            Damage = FormatNum(GetStr(p, "heroDamage")),
            Cure = FormatNum(GetStr(p, "cure")),
            Resist = FormatNum(GetStr(p, "resistDamage")),
            DamageMax = GetBool(p, "heroDamageMax"),
            CureMax = GetBool(p, "cureMax"),
            ResistMax = GetBool(p, "resistDamageMax"),
            TierText = brushKey is null ? "" : $"{cn}{GetLong(ri, "rank_sub_tier")}",
            TierIconLocal = brushKey is null ? null : await OwImageCache.GetAsync(_maps.RankIconUrl(GetStr(ri, "rank_name"))),
            FinalHit = GetLong(p, "finalHit").ToString(),
            TargetTime = Sec2Clock(GetLong(p, "targetCompetingTime")),
            HealingTaken = FormatNum(GetStr(p, "healingTaken")),
            DamageTaken = FormatNum(GetStr(p, "damageTaken")),
        };
        // 竞技对局天赋 perk(OW2 新增),复用技能图标
        foreach (var pk in FindArr(p, "perks")) await AddSkillAsync(mp, _maps.Perk(GetStr(pk, "guid")), true);
        return mp;
    }

    // 角斗单局详情:customer/fight/queryMatchInfo。数据在 totalCount 里,玩家名/token 走 nameMap;无禁用英雄。
    private async Task<MatchDetail?> LoadFightMatchDetailAsync(string ct, long roleId, string matchId)
    {
        var d = Parse(await _client.CustomerGetPathRawAsync("customer/fight/queryMatchInfo", ct, _ownBigdata!, _ownRoleId, ("matchId", matchId)));
        if (Find(d, "totalCount") is not { } total) return null;
        long ret = FindLong(d, "matchRet");
        var (mapName, mapIcon) = ResolveMap(GetStr(total, "mapGuid"), true);
        long secs = GetLong(total, "gameTimeSec");
        long start = GetLong(total, "startTime");
        var nameMap = Find(d, "nameMap");
        var md = new MatchDetail
        {
            MapName = mapName,
            MapIconLocal = await OwImageCache.GetAsync(mapIcon, 240),
            ModeText = "竞技角斗",
            ScoreText = $"{GetLong(total, "teamScore")} : {GetLong(total, "opponentScore")}",
            IsWin = ret == 1,
            ResultText = ret == 1 ? "胜利" : ret == -1 ? "失败" : "平局",
            Duration = Sec2Clock(secs),
            StartText = start > 0 ? DateTimeOffset.FromUnixTimeSeconds(start).LocalDateTime.ToString("MM-dd HH:mm") : "",
            EndText = start > 0 ? DateTimeOffset.FromUnixTimeSeconds(start + secs).LocalDateTime.ToString("HH:mm") : "",
        };
        // 先并发预取所有天赋/道具图标(去重,10人×10约100个但去重后更少),否则逐个下载会卡死界面
        var skillUrls = new HashSet<string>();
        foreach (var p in FindArr(total, "teammateList").Concat(FindArr(total, "enemyList")))
            foreach (var g in FindArr(p, "traitGuids").Concat(FindArr(p, "modGuids")))
            {
                var ic = _maps.TraitMod(AsStr(g))?.Icon;
                if (!string.IsNullOrEmpty(ic)) skillUrls.Add(ic);
            }
        await OwImageCache.PrefetchAsync(skillUrls, 128, 16);

        foreach (var p in FindArr(total, "teammateList")) md.Teammates.Add(await BuildFightPlayerAsync(p, roleId, nameMap));
        foreach (var p in FindArr(total, "enemyList")) md.Enemies.Add(await BuildFightPlayerAsync(p, roleId, nameMap));
        AssignGroups(FindArr(total, "teammateList").ToList(), md.Teammates);   // 组队判定
        AssignGroups(FindArr(total, "enemyList").ToList(), md.Enemies);

        // 多回合:每回合比分
        int rn = 1;
        foreach (var r in FindArr(d, "roundCountList"))
        {
            long ts = GetLong(r, "teamScore"), os = GetLong(r, "opponentScore");
            md.Rounds.Add(new RoundInfo { Label = $"第{rn++}回合", ScoreText = $"{ts}-{os}", IsWin = ts > os });
        }
        return md;
    }

    private async Task<MatchPlayer> BuildFightPlayerAsync(JsonElement p, long meRole, JsonElement? nameMap)
    {
        long id = GetLong(p, "bnetId");
        string name = nameMap is { ValueKind: JsonValueKind.Object } nm && nm.TryGetProperty(id.ToString(), out var nv) ? AsStr(nv) : "";
        var (disp, _) = SplitTag(name);
        var ri = GetObj(p, "rankInfo");
        var (cn, brushKey) = _maps.FightRank(GetStr(ri, "rankName"));   // 角斗段位
        var hg = GetStr(p, "heroGuid");
        var mp = new MatchPlayer
        {
            BnetId = id,
            IsMe = id == meRole,
            Name = disp,
            HeroIconLocal = await OwImageCache.GetAsync(_maps.HeroIcon(hg), 128),   // 角斗玩家无 heroIcon 字段,用 heroGuid 翻
            Kad = $"{GetLong(p, "kill")}/{GetLong(p, "assist")}/{GetLong(p, "death")}",
            Damage = FormatNum(GetStr(p, "heroDamage")),
            Cure = FormatNum(GetStr(p, "cure")),
            Resist = FormatNum(GetStr(p, "resistDamage")),
            DamageMax = GetBool(p, "heroDamageMax"),
            CureMax = GetBool(p, "cureMax"),
            ResistMax = GetBool(p, "resistDamageMax"),
            WorthMax = GetBool(p, "worthMax"),
            TierText = brushKey is null ? "" : $"{cn}{GetLong(ri, "rankSubTier")}",
            TierIconLocal = brushKey is null ? null : await OwImageCache.GetAsync(_maps.FightRankIcon(GetStr(ri, "rankName"))),
            FinalHit = GetLong(p, "finalHit").ToString(),
            TargetTime = Sec2Clock(GetLong(p, "targetCompetingTime")),
            HealingTaken = FormatNum(GetStr(p, "healingTaken")),
            DamageTaken = FormatNum(GetStr(p, "damageTaken")),
            Worth = FormatNum(GetStr(p, "worth")),   // 金钱/身价
        };
        // 技能:天赋(异能)+ 道具,翻译成图标
        foreach (var g in FindArr(p, "traitGuids")) await AddSkillAsync(mp, _maps.TraitMod(AsStr(g)), true);
        foreach (var g in FindArr(p, "modGuids")) await AddSkillAsync(mp, _maps.TraitMod(AsStr(g)), false);
        return mp;
    }

    private async Task AddSkillAsync(MatchPlayer mp, OwMappings.TraitModInfo? info, bool isTrait)
    {
        if (info is null || string.IsNullOrEmpty(info.Icon)) return;
        mp.Skills.Add(new SkillIcon
        {
            Name = info.Name,
            IconLocal = await OwImageCache.GetAsync(info.Icon, 128),  // 128:行内22px + 弹窗56px 放大都清晰
            IsTrait = isTrait,
            Desc = info.Desc,
            Category = info.Category,
            Level = info.Level,
        });
    }

    private static string Sec2Clock(long s) => s <= 0 ? "—" : $"{s / 60:00}:{s % 60:00}";

    // 组队标记色板(同队开黑标同色圆点)
    private static readonly Brush[] GroupBrushes = CreateGroupBrushes();
    private static Brush[] CreateGroupBrushes()
    {
        var hex = new[] { "#E8933D", "#4A9DE0", "#5FBF6A", "#C264C2", "#E05A6E", "#3DBFBF" };
        var arr = new Brush[hex.Length];
        for (int i = 0; i < hex.Length; i++)
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex[i])); b.Freeze(); arr[i] = b;
        }
        return arr;
    }

    // 用 friendBnetIds 做并查集,同一连通分量(≥2人)= 一个开黑队,标同色
    private static void AssignGroups(List<JsonElement> raw, List<MatchPlayer> built)
    {
        int n = raw.Count;
        if (n < 2) return;
        var ids = raw.Select(p => GetLong(p, "bnetId")).ToList();
        var idxOf = new Dictionary<long, int>();
        for (int i = 0; i < n; i++) idxOf[ids[i]] = i;
        var parent = new int[n];
        for (int i = 0; i < n; i++) parent[i] = i;
        int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
        for (int i = 0; i < n; i++)
            foreach (var fb in FindArr(raw[i], "friendBnetIds"))
                if (idxOf.TryGetValue(AsLong(fb), out int j)) parent[Find(i)] = Find(j);
        var comp = new Dictionary<int, List<long>>();
        for (int i = 0; i < n; i++) { int r = Find(i); if (!comp.TryGetValue(r, out var l)) comp[r] = l = new(); l.Add(ids[i]); }
        var color = new Dictionary<long, Brush>();
        int g = 0;
        foreach (var kv in comp)
            if (kv.Value.Count >= 2) { var b = GroupBrushes[g++ % GroupBrushes.Length]; foreach (var id in kv.Value) color[id] = b; }
        foreach (var p in built)
            if (color.TryGetValue(p.BnetId, out var b)) p.GroupColor = b;
    }

    /// <summary>该玩家各英雄的省内排行榜(customGetUserHeroBillboard)。useOpen=开放职责否则预设。</summary>
    public async Task<BillboardData> LoadBillboardAsync(long roleId, bool useOpen)
    {
        await EnsureReadyAsync();
        var ct = DashenClient.BuildCustomerToken(_sign!, roleId);
        var d = Parse(await _client.CustomerGetPathRawAsync("billboard/customGetUserHeroBillboard", ct, _ownBigdata!, _ownRoleId));
        string k1 = useOpen ? "sportOpenHeroBillboardList" : "sportPresetHeroBillboardList";
        string k2 = useOpen ? "sportPresetHeroBillboardList" : "sportOpenHeroBillboardList";
        var rows = FindArr(d, k1).OrderBy(r => GetLong(r, "rankNum")).ToList();
        if (rows.Count == 0) rows = FindArr(d, k2).OrderBy(r => GetLong(r, "rankNum")).ToList();

        var data = new BillboardData();
        int i = 1;
        foreach (var r in rows)
        {
            var hg = GetStr(r, "heroGuid");
            data.Rows.Add(new BillboardRow
            {
                Order = i++,
                HeroName = _maps.HeroName(hg),
                HeroIconLocal = await OwImageCache.GetAsync(_maps.HeroIcon(hg), 128),
                RankText = $"省内第 {GetLong(r, "rankNum")} 名",
                ScoreText = $"{GetLong(r, "rankedLevel")} 分",
                Extra = $"{GetLong(r, "matchSum")} 场 · {FormatPct(GetStr(r, "winRate"))}",
            });
        }
        if (rows.Count > 0)
        {
            var (nm, _) = SplitTag(GetStr(rows[0], "userName"));
            var prov = GetStr(rows[0], "province");
            data.SubTitle = string.IsNullOrEmpty(prov) ? nm : $"{nm} · {prov}";
        }
        return data;
    }

    // 头部(名字/头像/称号/赞赏/时长)——各模式通用
    /// <summary>某玩家的好友(roleId 驱动)。getBillboard(更全,需oauth)+ getFriendModule(任意号可查)两接口 × 全模式聚合去重,尽量凑全"有OW数据的好友"(无OW数据的社交好友接口不返回,大神本身也拿不到)。</summary>
    public async Task<FriendData> LoadFriendsAsync(long roleId, int season)
    {
        await EnsureReadyAsync();
        var token = await _client.GetReportTokenAsync(roleId) ?? "";
        var data = new FriendData();
        var seen = new HashSet<long>();
        string[] modes = { "SportPreset", "SportOpen", "LeisurePreset", "LeisureOpen", "SportFight", "LeisureFight" };
        foreach (var mode in modes)   // 排行榜更全,先它
            await AddFriendsAsync(data, seen, await Safe(_client.GetFriendBillboardRawAsync(token, roleId, season, mode)), "billboardList");
        foreach (var mode in modes)   // getFriendModule 补漏
            await AddFriendsAsync(data, seen, await Safe(_client.GetFriendModuleRawAsync(token, roleId, season, mode, 1, 100)), "friendList");
        data.Total = data.Rows.Count;
        return data;
    }

    private static async Task<string> Safe(Task<string> t) { try { return await t; } catch { return ""; } }

    private async Task AddFriendsAsync(FriendData data, HashSet<long> seen, string rawJson, string listKey)
    {
        if (string.IsNullOrEmpty(rawJson)) return;
        JsonElement d;
        try { d = Parse(rawJson); } catch { return; }
        foreach (var fr in FindArr(d, listKey))
        {
            long id = GetLong(fr, "bnetId");
            if (id == 0 || !seen.Add(id)) continue;   // 去重
            var ri = GetObj(fr, "rankInfo");
            var (cn, brushKey) = OwMappings.Rank(GetStr(ri, "rank_name"));
            long games = GetLong(fr, "matchCnt");
            double wr = GetDouble(fr, "winRatio") * 100;   // 小数 0~1
            long streak = GetLong(fr, "maxWinningStreak");
            data.Rows.Add(new FriendRow
            {
                BnetId = id,
                Name = GetStr(fr, "bnetAccountName"),
                AvatarLocal = await OwImageCache.GetAsync(GetStr(fr, "icon"), 100),
                TierText = brushKey is null ? "未定级" : $"{cn}{GetLong(ri, "rankSubTier")}",
                TierIconLocal = brushKey is null ? null : await OwImageCache.GetAsync(_maps.RankIconUrl(GetStr(ri, "rank_name"))),
                StatText = games > 0 ? $"{games}场 · 胜率{Math.Round(wr)}% · 最高连胜{streak}" : "本赛季暂无排位",
            });
        }
    }

    private async Task<PlayerStats> BuildHeaderAsync(JsonElement card)
    {
        var (disp, tag) = SplitTag(FindStr(card, "name"));
        int endorse = (int)FindLong(card, "level");
        string title = FindStr(card, "title");
        var ps = new PlayerStats
        {
            DisplayName = disp,
            TagSuffix = tag,
            Initial = disp.Length > 0 ? disp[..1] : "?",
            EndorseLevel = endorse,
            TotalHours = FormatHours(FindStr(card, "gameTime")),
            UpdatedAt = $"今天 {DateTime.Now:HH:mm}",
            HasTitle = !string.IsNullOrWhiteSpace(title),
            TitleText = title,
        };
        ps.AvatarLocal = await OwImageCache.GetAsync(FindStr(card, "icon"), 120);
        ps.TitleIconLocal = await OwImageCache.GetAsync(FindStr(card, "titleIcon"));
        ps.EndorseIconLocal = await OwImageCache.GetAsync(_maps.EndorseIconUrl(endorse));
        return ps;
    }

    private static void SetWeekPerf(PlayerStats ps, JsonElement? recent)
    {
        if (recent is { } r)
        {
            double rw = GetDouble(r, "winRate");
            ps.WeekPerf = rw >= 55 ? "优秀" : rw >= 48 ? "良好" : rw >= 40 ? "一般" : "需努力";
            ps.WeekDetail = $"近期 · 胜率{FormatPct(GetStr(r, "winRate"))} · 场均伤害{FormatNum(GetStr(r, "aveDamage"))}" +
                            $" · 场均击杀{GetLong(r, "aveKill")} · 场均治疗{FormatNum(GetStr(r, "aveCure"))}";
        }
    }

    // 社交口碑:gameAction(评价 + 获赞/送赞/被举报)
    private static void SetRepute(PlayerStats ps, JsonElement d)
    {
        if (Find(d, "gameAction") is { } ga)
        {
            ps.ReputeComment = GetStr(ga, "comment");
            ps.ReputeText = $"获赞 {GetLong(ga, "getHonorsCnt")} · 送赞 {GetLong(ga, "sendHonorsCnt")} · 被举报 {GetLong(ga, "reportedCnt")}";
        }
    }

    private async Task AddHeroesAsync(PlayerStats ps, List<JsonElement> heroes)
    {
        double maxM = heroes.Count > 0 ? Math.Max(1, heroes.Max(h => GetLong(h, "matchSum"))) : 1;
        foreach (var h in heroes)
        {
            var hg = GetStr(h, "heroGuid");
            long games = GetLong(h, "matchSum");
            var hri = GetObj(h, "heroRankInfo");   // 角斗英雄无此字段 → 无段位徽章
            var (hcn, hbrush) = OwMappings.Rank(GetStr(hri, "rankName"));
            long hrLvl = GetLong(hri, "rankedLevel");
            ps.Heroes.Add(new HeroStat
            {
                RankText = hbrush is null ? "" : $"{hcn}{GetLong(hri, "rankSubTier")}" + (hrLvl > 0 ? $" · {hrLvl}分" : ""),
                Name = _maps.HeroName(hg),
                IconLocal = await OwImageCache.GetAsync(_maps.HeroIcon(hg), 128),
                Detail = $"{Math.Round(GetLong(h, "gameTime") / 3600.0, 1)}h · {FormatPct(GetStr(h, "winRate"))}",
                PlayPercent = games / maxM * 100,
                HoursText = $"{Math.Round(GetLong(h, "gameTime") / 3600.0, 1)} 小时",
                WinRateText = FormatPct(GetStr(h, "winRate")),
                MatchsText = $"{games} 场",
                LevelText = GetLong(h, "heroLevel") > 0 ? $"Lv.{GetLong(h, "heroLevel")}" : "",
                DetailStats = BuildHeroStats(h),
                DetailStatsPerTen = BuildStatsFromMap(GetObj(h, "statPerTenMinCount")),
            });
        }
    }

    // 地图名+图标;角斗专属地图不在 ow_map_config 时回退到通用角斗图
    private (string Name, string Icon) ResolveMap(string mapGuid, bool isFight)
    {
        string name = _maps.MapName(mapGuid), icon = _maps.MapIcon(mapGuid);
        if (isFight && string.IsNullOrEmpty(icon))
        {
            icon = _maps.FightMapFallbackIcon;
            if (string.IsNullOrEmpty(name)) name = "角斗竞技场";
        }
        return (name, icon);
    }

    private async Task AddMatchAsync(ICollection<MatchRecord> into, JsonElement m, string? modeLabel = null, bool isFight = false)
    {
        var mg = GetStr(m, "mapGuid");
        var hg = GetStr(m, "heroGuid");
        long ret = GetLong(m, "matchRet");
        string heroIcon = GetStr(m, "heroIcon");
        if (string.IsNullOrEmpty(heroIcon)) heroIcon = _maps.HeroIcon(hg);
        var (mapName, mapIcon) = ResolveMap(mg, isFight);
        long ts = GetLong(m, "teamScore"), os = GetLong(m, "opponentScore");
        string score = (ts > 0 || os > 0) ? $"{ts}-{os}" : "";   // 快速模式 0-0 不显示
        into.Add(new MatchRecord
        {
            Result = ret == 1 ? "W" : ret == -1 ? "L" : "D",
            ScoreText = score,
            Mode = modeLabel ?? ModeCn(GetStr(m, "gameMode")),
            Map = mapName,
            MapIconLocal = await OwImageCache.GetAsync(mapIcon, 160),
            Hero = _maps.HeroName(hg),
            HeroIconLocal = await OwImageCache.GetAsync(heroIcon, 128),
            Kda = $"{GetLong(m, "kill")}/{GetLong(m, "death")}/{GetLong(m, "assist")}",
            TimeAgo = TimeAgo(GetLong(m, "beginTs")),
            MatchId = GetStr(m, "matchId"),
        });
    }

    private async Task<PlayerStats> BuildAsync(JsonElement card, JsonElement d, Func<string, Brush?> fb, string gameMode, bool useOpen)
    {
        var ps = await BuildHeaderAsync(card);
        SetWeekPerf(ps, Find(d, "recentMatchCount"));
        SetRepute(ps, d);

        bool sport = gameMode == "sport";
        ps.ShowRanks = sport;                                  // 休闲无段位,隐藏
        ps.IsOpenQueue = useOpen;
        ps.RankSectionTitle = useOpen ? "竞技段位 · 开放职责" : "竞技段位 · 预设职责";
        ps.AvgSectionTitle = $"场均表现 · {(sport ? "竞技" : "休闲")}{(useOpen ? "开放职责" : "预设职责")}";

        foreach (var g in FindArr(d, "guideCountData"))
        {
            var rr = BuildRank(g, fb);
            rr.TierIconLocal = await OwImageCache.GetAsync(_maps.RankIconUrl(GetStr(GetObj(g, "lastRankInfo"), "rank_name")));
            switch (GetStr(g, "roleType"))
            {
                case "tank": ps.Tank = rr; break;
                case "dps": ps.Dps = rr; break;
                case "healer": ps.Support = rr; break;
                case "open": ps.OpenRank = rr; break;   // 开放职责单段位
            }
        }

        // 预设职责 vs 开放职责:同一份返回里的两段
        string sumKey = useOpen ? "openSummaryData" : "presetsSummaryData";
        string heroKey = useOpen ? "openHeroUseSummaryList" : "presetsHeroUseSummaryList";
        if (Find(d, sumKey) is { } pv)
        {
            ps.AvgDamage = FormatNum(GetStr(pv, "aveHeroDamage"));
            ps.AvgHeal = FormatNum(GetStr(pv, "aveCure"));
            ps.AvgResist = FormatNum(GetStr(pv, "aveResistDamage"));
            ps.SeasonWinRate = FormatPct(GetStr(pv, "winRate"));
            ps.Kda = ComputeKda(pv);
            ps.WinLossText = $"{GetLong(pv, "matchWinSum")}胜 {GetLong(pv, "matchLossSum")}负";
            ps.AvgExtra = BuildAvgExtra(pv);
        }

        await AddHeroesAsync(ps, FindArr(d, heroKey).OrderByDescending(h => GetLong(h, "matchSum")).ToList());
        // 对局改由 queryMatchList 分页加载(StatsWindow.LoadAsync 拉第1页,滚动续拉),避免过滤错位
        return ps;
    }

    // 角斗领域(fight/queryCount)/ 休闲角斗(fight/getLeisureFightRoleCard)
    private async Task<PlayerStats> BuildFightAsync(JsonElement card, JsonElement d, Func<string, Brush?> fb, bool leisure)
    {
        var ps = await BuildHeaderAsync(card);
        SetWeekPerf(ps, Find(d, leisure ? "recentMatchData" : "recentMatchCount"));
        SetRepute(ps, d);

        ps.IsOpenQueue = false;
        ps.ShowRanks = !leisure;                               // 休闲角斗无段位
        ps.RankSectionTitle = "角斗段位";
        ps.AvgSectionTitle = leisure ? "场均表现 · 休闲角斗" : "场均表现 · 竞技角斗";

        // 角斗段位:roleTypeCountData(休闲无段位),段位名用 rankName 字段查
        foreach (var g in FindArr(d, "roleTypeCountData"))
        {
            var last = GetObj(g, "lastRankInfo");
            var max = GetObj(g, "maxRankInfo");
            var (cn, brushKey) = _maps.FightRank(GetStr(last, "rankName"));
            var rr = new RoleRank
            {
                TierText = brushKey is null ? "未定级" : $"{cn} {GetLong(last, "rankSubTier")}",
                ScoreText = brushKey is null ? "本赛季未定级" : $"{GetLong(last, "rankScore")} 分 · 最高 {GetLong(max, "rankScore")}",
                ExtraText = BuildRankExtra(g, fight: true),
                TierBrush = brushKey is null ? null : fb(brushKey),
                TierIconLocal = brushKey is null ? null : await OwImageCache.GetAsync(_maps.FightRankIcon(GetStr(last, "rankName"))),
            };
            switch (GetStr(g, "roleType"))
            {
                case "tank": ps.Tank = rr; break;
                case "dps": ps.Dps = rr; break;
                case "healer": ps.Support = rr; break;
            }
        }

        // 场均:角斗=summaryData,休闲角斗=seasonData
        if (Find(d, leisure ? "seasonData" : "summaryData") is { } sv)
        {
            ps.AvgDamage = FormatNum(GetStr(sv, "aveHeroDamage"));
            ps.AvgHeal = FormatNum(GetStr(sv, "aveCure"));
            ps.AvgResist = FormatNum(GetStr(sv, "aveResistDamage"));
            ps.SeasonWinRate = FormatPct(GetStr(sv, "winRate"));
            double k = GetDouble(sv, "aveKill"), a = GetDouble(sv, "aveAssist"), dth = GetDouble(sv, "aveDeath");
            ps.Kda = (dth <= 0 ? (k + a) : (k + a) / dth).ToString("0.00");
            ps.WinLossText = $"{GetLong(sv, "matchWinSum")}胜 {GetLong(sv, "matchLossSum")}负";
            ps.AvgExtra = BuildAvgExtra(sv);
        }

        await AddHeroesAsync(ps, FindArr(d, leisure ? "heroDataList" : "heroUseSummaryList").OrderByDescending(h => GetLong(h, "matchSum")).ToList());
        // 对局改由 queryMatchList 分页加载
        return ps;
    }

    private static RoleRank BuildRank(JsonElement g, Func<string, Brush?> fb)
    {
        var last = GetObj(g, "lastRankInfo");
        var max = GetObj(g, "maxRankInfo");
        var (cn, brushKey) = OwMappings.Rank(GetStr(last, "rank_name"));
        bool unranked = brushKey is null;
        return new RoleRank
        {
            TierText = unranked ? "未定级" : $"{cn} {GetLong(last, "rank_sub_tier")}",
            ScoreText = unranked ? "本赛季未定级" : $"{GetLong(last, "rankScore")} 分 · 最高 {GetLong(max, "rankScore")}",
            ExtraText = BuildRankExtra(g),
            TierBrush = brushKey is null ? null : fb(brushKey),
        };
    }

    // 每定位:两行(显式换行,不用 TextWrapping——避免 WPF 换行测量死循环)
    // 行1:场次 · 胜率  行2:KDA · 最高连胜(角斗再加场均金钱)
    private static string BuildRankExtra(JsonElement g, bool fight = false)
    {
        long games = GetLong(g, "matchSum");
        if (games <= 0) return "";
        string line1 = $"{games}场 · 胜率{FormatPct(GetStr(g, "winRate"))}";
        var l2 = new List<string>();
        var kda = GetStr(g, "kda"); if (kda.Length > 0) l2.Add($"KDA {kda}");
        long streak = GetLong(g, "maxWinStreak"); if (streak > 0) l2.Add($"连胜{streak}");
        if (fight) { long worth = GetLong(g, "aveWorth"); if (worth > 0) l2.Add($"金钱{FormatNum(worth.ToString())}"); }
        return l2.Count > 0 ? line1 + "\n" + string.Join(" · ", l2) : line1;
    }

    // 场均单独消灭/最后一击
    private static string BuildAvgExtra(JsonElement sv)
    {
        long ik = GetLong(sv, "aveIndividualKill"), fh = GetLong(sv, "aveFinalHit");
        var parts = new List<string>();
        if (ik > 0) parts.Add($"场均单独消灭 {ik}");
        if (fh > 0) parts.Add($"场均最后一击 {fh}");
        return string.Join(" · ", parts);
    }

    // ---- 英雄场均属性:翻译 + 排序 + 格式化 ----
    private static readonly string[] AttrOrder =
        { "消灭", "助攻", "阵亡", "单独消灭", "最终一击", "英雄伤害", "造成伤害", "伤害", "治疗量", "治疗", "承受伤害", "武器命中率", "命中率", "暴击", "多杀" };

    private List<StatItem> BuildHeroStats(JsonElement h) => BuildStatsFromMap(GetObj(h, "statAveCount"));

    // 把 {valueGuid: 数值} 的属性字典翻译成中文条目:该英雄有多少能翻译的就显示多少,按重要度排序(不截断)
    private List<StatItem> BuildStatsFromMap(JsonElement map)
    {
        if (map.ValueKind != JsonValueKind.Object) return new();
        var items = new List<(int prio, StatItem it)>();
        foreach (var p in map.EnumerateObject())
        {
            var name = _maps.AttrName(p.Name);
            if (name is null || name.Contains("游戏时间") || name.Contains("累计")) continue;
            double val = p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetDouble(out var n) ? n
                       : double.TryParse(p.Value.ToString(), out var m) ? m : 0;
            int prio = Array.FindIndex(AttrOrder, x => name.Contains(x));
            items.Add((prio < 0 ? 999 : prio, new StatItem { Name = name, Value = FormatStat(name, val) }));
        }
        // 稳定排序:通用重要数据在前,英雄专属数据在后,但一个都不丢
        return items.OrderBy(x => x.prio).Select(x => x.it).ToList();
    }

    private static string FormatStat(string name, double v)
    {
        if (name.Contains('率')) return $"{Math.Round(v * 100, 1)}%";     // 命中率等是 0~1 小数
        if (Math.Abs(v) >= 1000) return v.ToString("N0");
        if (Math.Abs(v) >= 100) return Math.Round(v).ToString();
        return Math.Round(v, 2).ToString();
    }

    // ---- 格式化 ----
    private static (string, string) SplitTag(string name)
    {
        int i = name.LastIndexOf('#');
        return i >= 0 ? (name[..i], name[i..]) : (name, "");
    }
    private static string FormatHours(string s) => double.TryParse(s, out var h) ? $"{Math.Round(h):N0} 小时" : (s + " 小时");
    private static string FormatNum(string s) => double.TryParse(s, out var n) ? n.ToString("N0") : s;
    private static string FormatPct(string s) => double.TryParse(s, out var n) ? $"{Math.Round(n)}%" : (s.Length > 0 ? s + "%" : "");
    private static string ComputeKda(JsonElement pv)
    {
        double k = GetDouble(pv, "aveKill"), a = GetDouble(pv, "aveAssist"), dth = GetDouble(pv, "aveDeath");
        return (dth <= 0 ? (k + a) : (k + a) / dth).ToString("0.00");
    }
    private static string ModeCn(string gm)
        => gm.Contains("Leisure", StringComparison.OrdinalIgnoreCase) ? "快速" : "竞技";
    private static string TimeAgo(long ts)
    {
        if (ts <= 0) return "";
        var t = DateTimeOffset.FromUnixTimeMilliseconds(ts).LocalDateTime;
        var span = DateTime.Now - t;
        if (span.TotalMinutes < 60) return $"{Math.Max(1, (int)span.TotalMinutes)}分钟前";
        if (span.TotalHours < 24) return $"{(int)span.TotalHours}小时前";
        if (span.TotalDays < 2) return "昨天";
        return $"{(int)span.TotalDays}天前";
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
    private static bool GetBool(JsonElement el, string prop)
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.True;
    private static double GetDouble(JsonElement el, string prop)
    {
        if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v))
        {
            if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var n)) return n;
            if (v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), out var m)) return m;
        }
        return 0;
    }
    private static string AsStr(JsonElement? e)
        => e is not { } v ? "" : v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" :
           v.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False ? v.ToString() : "";
    private static long AsLong(JsonElement? e)
    {
        if (e is not { } v) return 0;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n)) return n;
        if (v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), out var m)) return m;   // customer/* 返回数值为字符串
        return 0;
    }
    private static bool HasDetail(JsonElement? d) => FindArr(d, "guideCountData").Any() || FindArr(d, "matchList").Any();
}
