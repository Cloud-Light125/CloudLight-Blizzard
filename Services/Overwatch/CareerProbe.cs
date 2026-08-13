using System.Text;

namespace BnetSwitch.Services.Overwatch;

// --careerprobe "Player#1234"：免界面跑一遍「抓暴雪生涯页 → 解析 → 映射」，
// 把结果打成文本落盘,人工对着 https://overwatch.blizzard.com/en-us/career/<tag>/ 逐项比。
// 界面还没做之前,这是验证解析对不对的唯一手段。
public static class CareerProbe
{
    public static async Task RunAsync(StringBuilder sb, string battleTag)
    {
        if (string.IsNullOrWhiteSpace(battleTag))
        {
            sb.AppendLine("用法: BnetSwitch.exe --careerprobe \"Name#1234\"");
            return;
        }

        sb.AppendLine($"战网 ID: {battleTag}");
        sb.AppendLine(new string('=', 60));

        // ---- 第一段:HTTP ----
        var client = new BlizzardCareerClient();
        var t0 = DateTime.Now;
        var r = await client.LoadAsync(battleTag).ConfigureAwait(false);
        sb.AppendLine($"[HTTP] {r.Status}  耗时 {(DateTime.Now - t0).TotalSeconds:0.0}s  " +
                      $"permalink={r.Permalink ?? "(无)"}  html={(r.Html?.Length ?? 0):N0} 字符");
        if (r.Error != null) sb.AppendLine($"[HTTP] 说明: {r.Error}");

        // ---- 第二段:解析 ----
        // 抓不到也接着往下走:CareerService 那层还有「过期缓存兜底」和错误分支要验,
        // 断网时正是要看它们的表现。
        if (r.Status == BlizzardCareerClient.Status.Ok && r.Html != null)
        {
            var p = CareerParser.Parse(r.Html, battleTag);
            sb.AppendLine();
            sb.AppendLine($"[解析] 名字={p.Name}  赞赏={p.EndorseLevel}  更新于={p.LastUpdate?.ToLocalTime():yyyy-MM-dd HH:mm}");
            sb.AppendLine($"[解析] 头像={p.PortraitUrl}");
            sb.AppendLine($"[解析] 空档案={p.IsEmpty}  分段数={p.Views.Count}");

            foreach (var v in p.Views)
            {
                sb.AppendLine();
                sb.AppendLine($"── 输入设备: {v.Input}  {(v.HasData ? "" : "(无数据)")}");
                foreach (var rk in v.Ranks)
                {
                    var (cn, _) = OwMappings.Rank(rk.Tier);
                    sb.AppendLine($"   段位 {rk.Role,-8} {cn} {rk.Division}   ({rk.Tier} {rk.Division})");
                }
                foreach (var m in v.Modes.Values)
                {
                    sb.AppendLine($"   模式 {m.Name}: 进度条口径 {m.Comparisons.Count} 组, 英雄统计块 {m.Heroes.Count} 个");
                    foreach (var b in m.Bars(CareerParser.CatTimePlayed).Take(5))
                        sb.AppendLine($"      时长榜 {OwEnNames.Hero(b.Slug),-8} {b.Value,-12} {b.Percent:0.0}%");
                }
            }
        }

        // ---- 第三段:映射(界面真正要显示的东西)----
        var outcome = await new Stats.CareerService().LoadAsync(battleTag).ConfigureAwait(false);
        sb.AppendLine();
        sb.AppendLine(new string('=', 60));
        if (outcome.Profile is not { } prof)
        {
            sb.AppendLine($"[映射] 失败: {outcome.Error}  (NotFound={outcome.NotFound})");
            return;
        }

        // 界面默认落在哪个分段就验哪个:优先键鼠 + 竞技,没数据自动退到有数据的那份
        var pickInput = Stats.CareerService.PickInput(prof);
        var pickMode = Stats.CareerService.PickMode(prof, pickInput);
        sb.AppendLine($"[映射] 分段 输入={pickInput} 模式={pickMode}  " +
                      $"(键鼠={Stats.CareerService.HasData(prof, Stats.CareerService.InputPc)} " +
                      $"手柄={Stats.CareerService.HasData(prof, Stats.CareerService.InputPad)})");
        var ps = await Stats.CareerService.BuildAsync(prof, pickInput, pickMode, _ => null).ConfigureAwait(false);

        sb.AppendLine($"[映射] {ps.DisplayName}{ps.TagSuffix}  赞赏 {ps.EndorseLevel}  更新于 {ps.UpdatedAt}  缓存={outcome.FromCache}");
        sb.AppendLine($"[映射] 累计时长 {ps.TotalHours}");
        sb.AppendLine($"[映射] 段位  坦克 {ps.Tank.TierText} / 输出 {ps.Dps.TierText} / 治疗 {ps.Support.TierText}");
        sb.AppendLine($"[映射] {ps.AvgSectionTitle}: {ps.AvgDamageLabel} {ps.AvgDamage} · {ps.AvgHealLabel} {ps.AvgHeal} · " +
                      $"{ps.KdaLabel} {ps.Kda} · {ps.WinRateLabel} {ps.SeasonWinRate}({ps.WinLossText})");
        if (ps.HasAvgExtra) sb.AppendLine($"[映射] {ps.AvgExtra}");
        sb.AppendLine($"[映射] 头像本地缓存: {ps.AvatarLocal ?? "(无)"}");

        sb.AppendLine();
        sb.AppendLine($"[映射] 常用英雄 {ps.Heroes.Count} 个:");
        foreach (var h in ps.Heroes)
            sb.AppendLine($"   {h.Name,-8} {h.Detail,-20} {h.MatchsText,-8} 详情 {h.DetailStats.Count} 项 / 每10分钟 {h.DetailStatsPerTen.Count} 项");

        // 英雄详情下钻抽查:翻译没翻到的会原样留英文,这里把英文项挑出来,方便补译名表
        var first = ps.Heroes.FirstOrDefault();
        if (first != null)
        {
            sb.AppendLine();
            sb.AppendLine($"[映射] 「{first.Name}」详情全量:");
            foreach (var s in first.DetailStats) sb.AppendLine($"   {s.Name,-24} {s.Value}");
            foreach (var s in first.DetailStatsPerTen) sb.AppendLine($"   {s.Name,-24} {s.Value}");
        }

        var untranslated = ps.Heroes
            .SelectMany(h => h.DetailStats.Concat(h.DetailStatsPerTen))
            .Select(s => s.Name)
            .Where(n => n.Any(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z'))
            .Distinct().OrderBy(s => s).ToList();
        sb.AppendLine();
        sb.AppendLine($"[译名] 未翻译(仍是英文)的统计项 {untranslated.Count} 个:");
        foreach (var n in untranslated) sb.AppendLine("   " + n);
    }
}
