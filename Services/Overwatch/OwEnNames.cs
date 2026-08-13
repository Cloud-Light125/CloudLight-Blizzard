using System.Text.RegularExpressions;

namespace BnetSwitch.Services.Overwatch;

// 暴雪官方生涯页(overwatch.blizzard.com)只有英文/繁中等语言,没有简体。
// 这里把英文英雄名/统计项名翻成简体,用词尽量对齐国服官方译名
// (国服统计项名抄自网易大神 ow_hero_attr.json 的 valueText —— 已验证暴雪统计项 GUID
//  与网易 valueGuid 是同一套,例如 0x0860000000000021 == 603482350067646497 == 累计游戏时间)。
//
// 原则:查不到就原样返回英文。暴雪出新英雄/新统计项时宁可显示英文,也不显示空白。
public static class OwEnNames
{
    // ── 统计项后缀 ───────────────────────────────────────────────
    // 页面上的标签是「词根 - 后缀」,后缀只有 5 种。先剥后缀再查词根,
    // 表就不用从 260 个词根爆炸成 580 条完整标签。
    // 注意实测到的脏数据:大小写不一致(Avg per 10 min)、缺空格(Kills- Avg per 10 Min)、结尾多空格。
    private static readonly (Regex Re, string Prefix, string Suffix)[] Suffixes =
    {
        (new Regex(@"\s*-\s*Avg per 10 Min\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled), "每10分钟", ""),
        (new Regex(@"\s*-\s*Most in Game\s*$",   RegexOptions.IgnoreCase | RegexOptions.Compiled), "", " · 单场最多"),
        (new Regex(@"\s*-\s*Best in Game\s*$",   RegexOptions.IgnoreCase | RegexOptions.Compiled), "", " · 单场最佳"),
        (new Regex(@"\s*-\s*Most in Life\s*$",   RegexOptions.IgnoreCase | RegexOptions.Compiled), "", " · 单条命最多"),
        (new Regex(@"\s*-\s*Best\s*$",           RegexOptions.IgnoreCase | RegexOptions.Compiled), "", " · 最高"),
    };

    /// <summary>统计项英文标签 → 简体。剥掉「- Avg per 10 Min」这类后缀后查表,查不到返回原文。</summary>
    public static string Stat(string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return string.Empty;
        var raw = label.Trim();
        foreach (var (re, pre, suf) in Suffixes)
        {
            var m = re.Match(raw);
            if (!m.Success) continue;
            var root = raw[..m.Index].Trim();
            return StatTable.TryGetValue(root, out var cn) ? pre + cn + suf : raw;
        }
        return StatTable.TryGetValue(raw, out var v) ? v : raw;
    }

    /// <summary>统计项分类标题(Combat / Average / …)→ 简体。</summary>
    public static string Category(string? name)
        => !string.IsNullOrWhiteSpace(name) && CategoryTable.TryGetValue(name.Trim(), out var v) ? v : (name ?? string.Empty).Trim();

    /// <summary>英雄 slug(genji / soldier-76 / wrecking-ball)或英文名 → 简体。查不到返回原文。</summary>
    public static string Hero(string? slugOrName)
    {
        if (string.IsNullOrWhiteSpace(slugOrName)) return string.Empty;
        var k = slugOrName.Trim();
        if (HeroTable.TryGetValue(k, out var v)) return v;
        // 传进来的是显示名(Soldier: 76 / Lúcio / Wrecking Ball)时,归一成 slug 再查
        return HeroTable.TryGetValue(Slug(k), out var v2) ? v2 : k;
    }

    // ── 英雄:slug → 简体 ────────────────────────────────────────
    // slug 抄自生涯页 data-hero-id,简体抄自网易 ow_hero_config.json 的 name。
    // vendetta 用发布顺序定位:生涯页下拉里排在 wuyang(0x02E00000000003C3)与
    // sierra(0x02E00000000004D2)之间,网易配置里只有「斩仇」(0x02E0000000000472)落在这个区间。
    private static readonly Dictionary<string, string> HeroTable = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ana"] = "安娜",
        ["ashe"] = "艾什",
        ["baptiste"] = "巴蒂斯特",
        ["bastion"] = "堡垒",
        ["brigitte"] = "布丽吉塔",
        ["cassidy"] = "卡西迪",
        ["doomfist"] = "末日铁拳",
        ["dva"] = "D.Va",
        ["d-va"] = "D.Va",
        ["echo"] = "回声",
        ["emre"] = "埃姆雷",
        ["freja"] = "弗蕾娅",
        ["genji"] = "源氏",
        ["hanzo"] = "半藏",
        ["hazard"] = "骇灾",
        ["illari"] = "伊拉锐",
        ["jetpack-cat"] = "飞天猫",
        ["junker-queen"] = "渣客女王",
        ["junkrat"] = "狂鼠",
        ["juno"] = "朱诺",
        ["kiriko"] = "雾子",
        ["lifeweaver"] = "生命之梭",
        ["lucio"] = "卢西奥",
        ["mauga"] = "毛加",
        ["mei"] = "美",
        ["mercy"] = "天使",
        ["moira"] = "莫伊拉",
        ["orisa"] = "奥丽莎",
        ["pharah"] = "法老之鹰",
        ["ramattra"] = "拉玛刹",
        ["reaper"] = "死神",
        ["reinhardt"] = "莱因哈特",
        ["roadhog"] = "路霸",
        ["sierra"] = "西拉",
        ["sigma"] = "西格玛",
        ["sojourn"] = "索杰恩",
        ["soldier-76"] = "士兵：76",
        ["sombra"] = "黑影",
        ["symmetra"] = "秩序之光",
        ["torbjorn"] = "托比昂",
        ["tracer"] = "猎空",
        ["vendetta"] = "斩仇",
        ["venture"] = "探奇",
        ["widowmaker"] = "黑百合",
        ["winston"] = "温斯顿",
        ["wrecking-ball"] = "破坏球",
        ["wuyang"] = "无漾",
        ["zarya"] = "查莉娅",
        ["zenyatta"] = "禅雅塔",
        ["all-heroes"] = "全部英雄",
    };

    // 生涯页下拉里给的是显示名(Soldier: 76 / Lúcio / D.Va),进度条上给的是 slug(soldier-76 / lucio / dva)。
    // 大部分显示名机械地小写+连字符化就等于 slug,这里只列机械转换会翻车的几个。
    private static readonly Dictionary<string, string> NameSlugFix = new(StringComparer.OrdinalIgnoreCase)
    {
        ["D.Va"] = "dva",
        ["Lúcio"] = "lucio",
        ["Torbjörn"] = "torbjorn",
        ["Soldier: 76"] = "soldier-76",
        ["ALL HEROES"] = "all-heroes",
        ["All Heroes"] = "all-heroes",
    };

    /// <summary>英雄显示名 → slug(用于对上进度条里的 data-hero-id / 图标)。</summary>
    public static string Slug(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName)) return string.Empty;
        var k = displayName.Trim();
        if (NameSlugFix.TryGetValue(k, out var fixd)) return fixd;
        return Regex.Replace(k.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
    }

    // ── 统计项分类 ──────────────────────────────────────────────
    private static readonly Dictionary<string, string> CategoryTable = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Combat"] = "战斗",
        ["Average"] = "场均",
        ["Best"] = "最佳",
        ["Assists"] = "助攻",
        ["Game"] = "对局",
        ["Match Awards"] = "比赛奖章",
        ["Hero Specific"] = "英雄专属",
    };

    // ── 统计项词根 → 简体 ───────────────────────────────────────
    // 单复数两种写法页面上都出现过(Elimination / Eliminations),一律各收一条。
    private static readonly Dictionary<string, string> StatTable = new(StringComparer.OrdinalIgnoreCase)
    {
        // 通用:战斗
        ["Eliminations"] = "消灭",
        ["Elimination"] = "消灭",
        ["Eliminations per Life"] = "每条命消灭数",
        ["Elimination per Life"] = "每条命消灭数",
        ["Final Blows"] = "最后一击",
        ["Final Blow"] = "最后一击",
        ["Deaths"] = "阵亡",
        ["Death"] = "阵亡",
        ["Assists"] = "助攻",
        ["Assist"] = "助攻",
        ["Defensive Assists"] = "防御助攻",
        ["Defensive Assist"] = "防御助攻",
        ["Offensive Assists"] = "攻击助攻",
        ["Offensive Assist"] = "攻击助攻",
        ["Recon Assists"] = "侦测助攻",
        ["Recon Assist"] = "侦测助攻",
        ["Damage Done"] = "伤害量",
        ["All Damage Done"] = "总伤害量",
        ["Hero Damage Done"] = "对英雄伤害量",
        ["Barrier Damage Done"] = "对屏障伤害量",
        ["Healing Done"] = "治疗量",
        ["Self Healing"] = "自我治疗量",
        ["Damage Amplified"] = "强化伤害",
        ["Healing Amplified"] = "强化治疗量",
        ["Damage Deflected"] = "反弹伤害量",
        ["Damage Reflected"] = "反弹伤害量",
        ["Damage Blocked"] = "抵挡伤害量",
        ["Damage Mitigated"] = "减免伤害量",
        ["Objective Kills"] = "目标点消灭",
        ["Objective Kill"] = "目标点消灭",
        ["Objective Time"] = "目标点时间",
        ["Objective Contest Time"] = "目标争夺时间",
        ["Obj Contest Time"] = "目标争夺时间",
        ["Solo Kills"] = "单独消灭",
        ["Solo Kill"] = "单独消灭",
        ["Melee Final Blows"] = "近战最后一击",
        ["Melee Final Blow"] = "近战最后一击",
        ["Melee Kills"] = "近战消灭",
        ["Environmental Kills"] = "地形消灭",
        ["Environmental Kill"] = "地形消灭",
        ["Knockback Kills"] = "击退消灭",
        ["Knockback Kill"] = "击退消灭",
        ["Jump Kills"] = "跳跃消灭",
        ["Kill Streak"] = "连续消灭",
        ["Multikill"] = "多重消灭",
        ["Multikills"] = "多重消灭",
        ["Long Range Final Blows"] = "远距离最后一击",
        ["Long Range Final Blow"] = "远距离最后一击",
        ["Critical Hits"] = "暴击",
        ["Critical Hit"] = "暴击",
        ["Critical Hit Accuracy"] = "暴击命中率",
        ["Critical Hit Damage"] = "暴击伤害",
        ["Critical Hit Kills"] = "暴击消灭",
        ["Critical Hit Kill"] = "暴击消灭",
        ["Weapon Accuracy"] = "武器命中率",
        ["Weapon Kills"] = "武器消灭",
        ["Primary Fire Accuracy"] = "主要攻击模式命中率",
        ["Secondary Fire Accuracy"] = "辅助攻击模式命中率",
        ["Secondary Direct Hits"] = "辅助攻击直接命中",
        ["Direct Hit Accuracy"] = "直接命中率",
        ["Scoped Accuracy"] = "开镜命中率",
        ["Unscoped Accuracy"] = "非开镜命中率",
        ["Scoped Critical Hit Accuracy"] = "开镜暴击率",
        ["Scoped Critical Hits"] = "开镜暴击",
        ["Scoped Critical Hit Kills"] = "开镜暴击消灭",
        ["Ultimate Damage"] = "终极技能伤害量",
        ["Ultimates Negated"] = "拦截终极技能次数",
        ["Ultimate Negated"] = "拦截终极技能次数",
        ["Ultimates Reflected"] = "终极技能反弹",
        ["Ultimate Reflected"] = "终极技能反弹",
        ["Average Energy"] = "平均能量",
        ["Average Damage Multiplier"] = "平均伤害系数",
        ["Airtime Percentage"] = "滞空时间占比",
        ["Shields Created"] = "生成护盾",
        ["Overhealth Created"] = "生成过量生命值",
        ["Overhealth Provided"] = "提供过量生命值",
        ["Negative Effects Cleansed"] = "负面效果净化",
        ["Players Resurrected"] = "复活玩家",
        ["Players Teleported"] = "传送玩家",
        ["Players Knocked Back"] = "击退玩家",
        ["Players Saved"] = "拯救玩家",
        ["Enemies Frozen"] = "冰冻敌人",
        ["Enemies Hacked"] = "侵入敌人",
        ["Enemies Slept"] = "麻醉敌人",
        ["Enemies Trapped"] = "困住敌人",

        // 通用:对局
        ["Time Played"] = "累计游戏时间",
        ["Games Played"] = "对战场次",
        ["Games Won"] = "胜场",
        ["Games Lost"] = "败场",
        ["Win Percentage"] = "胜率",
        ["Hero Wins"] = "英雄获胜",
        ["Time Spent on Fire"] = "最佳表现时间",
        ["of Match on Fire"] = "最佳表现时间占比",
        ["Cards"] = "表扬卡",
        ["Card"] = "表扬卡",
        ["Medals"] = "奖章",
        ["Medals - Gold"] = "金牌",
        ["Medals - Silver"] = "银牌",
        ["Medals - Bronze"] = "铜牌",

        // 英雄专属(按英雄字母序大致排列;技能名用国服官方译名)
        ["Accretion Accuracy"] = "质量吸附命中率",
        ["Accretion Kills"] = "质量吸附消灭",
        ["Adaptive Shielding Created"] = "生成感应护盾",
        ["Ally Coalescence Efficiency"] = "对盟友聚合射线效率",
        ["Enemy Coalescence Efficiency"] = "对敌人聚合射线效率",
        ["Coalescence Healing"] = "聚合射线治疗量",
        ["Coalescence Kills"] = "聚合射线消灭",
        ["Amplification Matrix Assists"] = "增幅矩阵助攻",
        ["Annihilation Efficiency"] = "毁天灭地效率",
        ["Annihilation Kills"] = "毁天灭地消灭",
        ["Artillery Kills"] = "火炮模式消灭",
        ["Assault Kills"] = "强攻模式消灭",
        ["Recon Kills"] = "侦察模式消灭",
        ["Tank Kills"] = "坦克模式消灭",
        ["Barrage Kills"] = "弹幕消灭",
        ["Biotic Grenade Kills"] = "生物手雷消灭",
        ["Biotic Orb Kills"] = "生化之球消灭",
        ["Biotic Orb Kill"] = "生化之球消灭",
        ["Biotic Field Healing"] = "生物力场治疗量",
        ["Blaster Kills"] = "光枪消灭",
        ["Blizzard Kills"] = "暴雪消灭",
        ["Bob Kills"] = "鲍勃消灭",
        ["Bola Shot Damage Done"] = "流星索伤害量",
        ["Burrow Kills"] = "钻地消灭",
        ["Cage Fight Kills"] = "笼中斗消灭",
        ["Call Mech Kills"] = "呼叫机甲消灭",
        ["Captive Sun Damage"] = "万千烈日伤害量",
        ["Carnage Kills"] = "血斩消灭",
        ["Chain Hook Accuracy"] = "链钩命中率",
        ["Chain Hook Kills"] = "链钩消灭",
        ["Charge Kills"] = "冲锋消灭",
        ["Charged Shot Accuracy"] = "充能射击命中率",
        ["Charged Shot Critical Accuracy"] = "充能射击暴击率",
        ["Charged Shot Kills"] = "充能射击消灭",
        ["Charged Volley Accuracy"] = "蓄力齐射命中率",
        ["Charged Volley Kills"] = "蓄力齐射消灭",
        ["Coach Gun Kills"] = "冲击枪消灭",
        ["Concussion Mine Kills"] = "震荡地雷消灭",
        ["Cyber Frag Damage Done"] = "赛博手雷伤害量",
        ["Deadeye Kills"] = "神射手消灭",
        ["Death Blossom Kills"] = "死亡绽放消灭",
        ["Disruptor Shot Kills"] = "干扰弹消灭",
        ["Disruptor Shot Kill"] = "干扰弹消灭",
        ["Downpour Kills"] = "暴雨消灭",
        ["Dragonblade Kills"] = "龙刃消灭",
        ["Dragonstrike Kills"] = "龙击炮消灭",
        ["Drill Dash Kills"] = "钻头突刺消灭",
        ["Duplicate Kills"] = "人格复制消灭",
        ["Dynamite Kills"] = "延时雷管消灭",
        ["EMP Kills"] = "电磁脉冲消灭",
        ["Earthshatter Kills"] = "裂地猛击消灭",
        ["Earthshatter Kill"] = "裂地猛击消灭",
        ["Earthshatter Stuns"] = "裂地猛击击晕",
        ["Earthshatter Direct Hits"] = "裂地猛击直接命中",
        ["Earthshatter Direct Hit"] = "裂地猛击直接命中",
        ["Energy Javelin Accuracy"] = "能量标枪命中率",
        ["Energy Javelin Kills"] = "能量标枪消灭",
        ["Fan the Hammer Kills"] = "连射消灭",
        ["Fire Strike Accuracy"] = "烈焰打击命中率",
        ["Fire Strike Kills"] = "烈焰打击消灭",
        ["Flashbang Kills"] = "闪光弹消灭",
        ["Focusing Beam Accuracy"] = "聚焦光线命中率",
        ["Focusing Beam Kills"] = "聚焦光线消灭",
        ["Grappling Claw Kills"] = "工程抓钩消灭",
        ["Grappling Claw Kill"] = "工程抓钩消灭",
        ["Gravitic Flux Kills"] = "引力乱流消灭",
        ["Graviton Surge Kills"] = "重力喷涌消灭",
        ["Guardian Wave Kills"] = "守护之浪消灭",
        ["Healing Accuracy"] = "治疗命中率",
        ["Healing Beam Usage"] = "治疗光束使用率",
        ["Offensive Beam Usage"] = "攻击光束使用率",
        ["Healing Boost Usage"] = "治愈音效使用率",
        ["Speed Boost Usage"] = "加速音效使用率",
        ["Helix Rocket Accuracy"] = "螺旋飞弹命中率",
        ["Helix Rocket Kills"] = "螺旋飞弹消灭",
        ["High Energy Kills"] = "高能消灭",
        ["Icicle Accuracy"] = "冰锥命中率",
        ["Icicle Critical Accuracy"] = "冰锥暴击率",
        ["Immortality Field Deaths Prevented"] = "维生力场阻止死亡",
        ["Inspire Uptime Percentage"] = "鼓舞士气持续时间占比",
        ["Jagged Blade Accuracy"] = "锯齿利刃命中率",
        ["Jagged Blade Kills"] = "锯齿利刃消灭",
        ["Jagged Wall Assists"] = "尖刺墙助攻",
        ["Javelin Spin Kills"] = "标枪旋击消灭",
        ["Jump Pack Kills"] = "喷射背包消灭",
        ["Kitsune Rush Assists"] = "御狐之姿助攻",
        ["Kunai Kills"] = "苦无消灭",
        ["Life Grip Deaths Prevented"] = "生命之握阻止死亡",
        ["Life Grip Savess"] = "生命之握拯救",
        ["Life Grip Saves"] = "生命之握拯救",
        ["Lifeline Healing"] = "救生索治疗量",
        ["Lifeline Usage"] = "救生索使用率",
        ["Low Health Recalls"] = "低生命值闪回",
        ["Low Health Teleports"] = "低生命值位移传动",
        ["Meteor Strike Kills"] = "流星坠击消灭",
        ["Meteor Strike Kill"] = "流星坠击消灭",
        ["Micro Missile Kills"] = "微型飞弹消灭",
        ["Nano Boost Assists"] = "纳米激素助攻",
        ["Nano Boost Assist"] = "纳米激素助攻",
        ["Onslaught Uptime Percentage"] = "猛攻持续时间占比",
        ["Orbital Ray Assists"] = "轨道射线助攻",
        ["Orbital Ray Assist"] = "轨道射线助攻",
        ["Orbital Ray Healing"] = "轨道射线治疗量",
        ["Overclock Kills"] = "机体超频消灭",
        ["Override Protocol Kills"] = "覆盖协议消灭",
        ["Overrun Kills"] = "蛮力冲撞消灭",
        ["Primal Rage Damage"] = "原始暴怒伤害量",
        ["Primal Rage Kills"] = "原始暴怒消灭",
        ["Projected Edge Accuracy"] = "锋锐剑气命中率",
        ["Projected Edge Kills"] = "锋锐剑气消灭",
        ["Pulse Bomb Attach Rate"] = "脉冲炸弹命中率",
        ["Pulse Bomb Kills"] = "脉冲炸弹消灭",
        ["Pulse Bombs Attached"] = "脉冲炸弹命中",
        ["Pummel Accuracy"] = "猛拳命中率",
        ["Pummel Kills"] = "猛拳消灭",
        ["Purr Healing"] = "呼噜噜治疗量",
        ["Pylon Healing"] = "光塔治疗量",
        ["Pylon Uptime Percentage"] = "光塔持续时间占比",
        ["RIP-Tire Kills"] = "炸弹轮胎消灭",
        ["Rampage Kills"] = "杀戮狂宴消灭",
        ["Rampage Kill"] = "杀戮狂宴消灭",
        ["Ravenous Vortex Kills"] = "吞噬漩涡消灭",
        ["Rocket Direct Hits"] = "火箭直接命中",
        ["Rocket Punch Kills"] = "火箭重拳消灭",
        ["Scoped Kills"] = "开镜消灭",
        ["Seismic Slam Kills"] = "裂地重拳消灭",
        ["Self-Destruct Kills"] = "自毁消灭",
        ["Sentry Turret Kills"] = "哨戒炮消灭",
        ["Siphon Blaster Damage Done"] = "虹吸冲击枪伤害量",
        ["Sleep Dart Accuracy"] = "麻醉镖命中率",
        ["Smart Excavator Direct Hits"] = "智能挖掘机直接命中",
        ["Sound Barriers Provided"] = "提供音障",
        ["Sound Barrier Damage Mitigated"] = "音障减免伤害量",
        ["Spike Guard Damage Done"] = "尖刺护体伤害量",
        ["Sticky Bombs Direct Hit Accuracy"] = "黏性炸弹直接命中率",
        ["Sticky Bombs Direct Hits"] = "黏性炸弹直接命中",
        ["Sticky Bombs Kills"] = "黏性炸弹消灭",
        ["Storm Arrow Kills"] = "疾风箭消灭",
        ["Sundering Blade Kills"] = "斩地巨剑消灭",
        ["Sunstruck Detonations"] = "日灼爆炸",
        ["Swift Strike Resets"] = "“影”重置",
        ["Tactical Grenade Kills"] = "战术榴弹消灭",
        ["Tactical Visor Kills"] = "战术目镜消灭",
        ["Take Aim Accuracy"] = "瞄准射击命中率",
        ["Take Aim Kills"] = "瞄准射击消灭",
        ["Tectonic Shock Kills"] = "地壳震击消灭",
        ["Terra Surge Kills"] = "撼地猛刺消灭",
        ["Thorn Volley Kills"] = "棘刺箭雨消灭",
        ["Tidal Blast Kills"] = "惊涛破消灭",
        ["Tidal Blast Kill"] = "惊涛破消灭",
        ["Tracking Kills"] = "追踪弹消灭",
        ["Tracking Shot Accuracy"] = "追踪弹命中率",
        ["Trailblazer Kills"] = "开路先锋消灭",
        ["Transcendence Efficiency"] = "超凡入圣效率",
        ["Transcendence Healing"] = "超凡入圣治疗量",
        ["Tree of Life Healing"] = "生命之树治疗量",
        ["Turret Kills"] = "炮台消灭",
        ["Valkyrie Damage Done"] = "女武神伤害量",
        ["Valkyrie Healing Done"] = "女武神治疗量",
        ["Venom Mine Kills"] = "地雷禁区消灭",
        ["Violent Leap Damage Done"] = "猛跃伤害量",
        ["Virus Accuracy"] = "病毒侵染命中率",
        ["Virus Kills"] = "病毒侵染消灭",
        ["Water Staff Direct Hits"] = "玄武之杖直接命中",
        ["Whipshot Accuracy"] = "流星飞锤命中率",
        ["Whipshots Attempted"] = "流星飞锤尝试次数",
        ["Whirlwind Dash Kills"] = "旋风冲刺消灭",
        ["Whole Hog Kills"] = "轰翻天消灭",
        ["Whole Hog Kill"] = "轰翻天消灭",
        ["Wound Uptime Percentage"] = "创伤持续时间占比",
    };

    /// <summary>标签是不是暴雪页面上那批未实装占位(名字以 NYI 结尾)。这些条目要整条丢掉。</summary>
    public static bool IsPlaceholder(string? label)
        => !string.IsNullOrWhiteSpace(label) && label.TrimEnd().EndsWith("NYI", StringComparison.Ordinal);
}
