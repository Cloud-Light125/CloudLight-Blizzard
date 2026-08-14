using CloudLightBlizzard.Services.OverwatchRegion;

namespace CloudLightBlizzard.ViewModels;

public enum RegionPreparationState
{
    NotPrepared,
    PreparingCurrentRegion,
    WaitingForOtherRegion,
    BuildingBackup,
    Ready,
    Outdated,
    Mixed,
    SwitchingRegion,
    ValidatingBackup,
    Error,
}

public enum RegionOperationPhase
{
    None,
    PreparingCurrentRegion,
    BuildingBackup,
    SwitchingRegion,
    ValidatingBackup,
}

public enum RegionPreparationAction
{
    ChooseChina,
    ChooseInternational,
    ContinueOtherRegion,
    Cancel,
    Validate,
    Restart,
    RestoreChina,
    RestoreInternational,
    Retry,
}

public sealed class RegionPreparationGuide
{
    public RegionPreparationState State { get; init; }
    public string CurrentFileText { get; init; } = "尚未识别";
    public string ChinaBackupText { get; init; } = "尚未准备";
    public string InternationalBackupText { get; init; } = "尚未准备";
    public string BackupStateText { get; init; } = "尚未准备";
    public string BackupSizeText { get; init; } = "0 个文件 · 0.0 KB";
    public string DifferenceText { get; init; } = "0 个文件";
    public string BackupBytesText { get; init; } = "0.0 KB";
    public string GamePathText { get; init; } = "尚未设置";
    public string BackupPathText { get; init; } = "";

    public string StepText { get; init; } = "";
    public string Title { get; init; } = "区服文件准备";
    public string Description { get; init; } = "";
    public string Notice { get; init; } = "";
    public string PreparationRouteText { get; init; } = "";
    public string FirstStepCompleteText { get; init; } = "";
    public string SecondStepCompleteText { get; init; } = "";
    public string ContinueButtonText { get; init; } = "继续";
    public string ProgressText { get; init; } = "正在准备区服文件…";
    public double ProgressCurrent { get; init; }
    public double ProgressTotal { get; init; } = 1;
    public bool ProgressIndeterminate { get; init; }

    public string SwitchChinaText { get; init; } = "恢复到国服";
    public string SwitchInternationalText { get; init; } = "恢复到国际服";
    public bool CanSwitchChina { get; init; }
    public bool CanSwitchInternational { get; init; }
    public bool CanChangePaths { get; init; }
    public bool CanClear { get; init; }
    public bool CanChooseCurrentRegion { get; init; }
    public bool CanContinueOtherRegion { get; init; }
    public bool CanCancel { get; init; }
    public bool CanValidate { get; init; }
    public bool CanRestart { get; init; }
    public bool CanRestore { get; init; }
    public bool CanRetry { get; init; }

    public bool ShowTopRegionActions { get; init; }
    public bool ShowNotPrepared { get; init; }
    public bool ShowWaiting { get; init; }
    public bool ShowProgress { get; init; }
    public bool ShowCompletedPreparationSteps { get; init; }
    public bool ShowReady { get; init; }
    public bool ShowOutdated { get; init; }
    public bool ShowMixed { get; init; }
    public bool ShowError { get; init; }
    public bool ShowAdvanced { get; init; }
    public bool ShowGamePathRequired { get; init; }
    public bool ShowNotice { get; init; }
    public bool ShowSuccessNotice { get; init; }
    public IReadOnlyList<RegionPreparationAction> VisibleActions { get; init; } = Array.Empty<RegionPreparationAction>();

    public static RegionPreparationGuide Create(
        RegionSnapshotStatus status,
        RegionOperationPhase operation,
        bool restartRequested,
        bool busy,
        RegionProgress? progress,
        string backupRoot,
        string notice = "",
        string error = "",
        OverwatchRegion? operationSource = null)
    {
        var state = ResolveState(status, operation, restartRequested, error);
        var source = status.PendingSourceRegion ?? operationSource;
        var target = status.PendingTargetRegion ?? (operationSource is null ? null :
            operationSource == OverwatchRegion.China ? OverwatchRegion.International : OverwatchRegion.China);
        var targetName = RegionName(target);
        var current = status.CurrentRegion;
        var isOperationallyBusy = operation != RegionOperationPhase.None || busy;
        var actions = ActionsFor(state);

        var (step, title, description) = CopyFor(state, source, target);
        if (status.State == RegionBackupState.Legacy && state == RegionPreparationState.Outdated)
        {
            title = "区服文件需要重新准备";
            description = "区服文件功能已经升级，当前备份无法继续使用。请重新准备一次区服文件。\n\n旧备份会保留到新的准备过程成功完成，不会在开始准备时立即删除。";
        }
        if (!string.IsNullOrWhiteSpace(error)) description = error;

        return new RegionPreparationGuide
        {
            State = state,
            CurrentFileText = CurrentRegionName(current),
            ChinaBackupText = BackupName(status.ChinaBackupComplete, status.ChinaCaptured),
            InternationalBackupText = BackupName(status.InternationalBackupComplete, status.InternationalCaptured),
            BackupStateText = BackupStateName(status.State),
            BackupSizeText = $"{status.DifferenceCount:N0} 个文件 · {FormatBytes(status.BackupBytes)}",
            DifferenceText = $"{status.DifferenceCount:N0} 个文件",
            BackupBytesText = FormatBytes(status.BackupBytes),
            GamePathText = string.IsNullOrWhiteSpace(status.GamePath) ? "尚未设置" : status.GamePath,
            BackupPathText = backupRoot,
            StepText = step,
            Title = title,
            Description = description,
            Notice = notice,
            PreparationRouteText = source is null || target is null ? "" : $"当前准备：{RegionName(source)} → {targetName}",
            FirstStepCompleteText = source is null ? "" : $"✓ 步骤 1　{RegionName(source)}文件已保存",
            SecondStepCompleteText = target is null ? "" : $"✓ 步骤 2　{targetName}更新已确认",
            ContinueButtonText = string.IsNullOrWhiteSpace(notice) ? $"我已完成{targetName}更新" : "重新检查",
            ProgressText = progress?.Message ?? ProgressFallback(state, source),
            ProgressCurrent = progress is { BytesTotal: > 0 } ? progress.BytesCurrent : progress?.Current ?? 0,
            ProgressTotal = Math.Max(1, progress is { BytesTotal: > 0 } ? progress.BytesTotal : progress?.Total ?? 1),
            ProgressIndeterminate = progress is null || progress.BytesTotal <= 0 && progress.Total <= 0,
            SwitchChinaText = current == CurrentGameRegion.China ? "当前为国服" :
                current == CurrentGameRegion.International ? "切换到国服" : "恢复到国服",
            SwitchInternationalText = current == CurrentGameRegion.International ? "当前为国际服" :
                current == CurrentGameRegion.China ? "切换到国际服" : "恢复到国际服",
            CanSwitchChina = !isOperationallyBusy && status.GamePathValid && state is RegionPreparationState.Ready or RegionPreparationState.Mixed && current != CurrentGameRegion.China,
            CanSwitchInternational = !isOperationallyBusy && status.GamePathValid && state is RegionPreparationState.Ready or RegionPreparationState.Mixed && current != CurrentGameRegion.International,
            CanChangePaths = !isOperationallyBusy,
            CanClear = !isOperationallyBusy && state == RegionPreparationState.Ready,
            CanChooseCurrentRegion = !isOperationallyBusy && state == RegionPreparationState.NotPrepared && status.GamePathValid,
            CanContinueOtherRegion = !isOperationallyBusy && state == RegionPreparationState.WaitingForOtherRegion,
            CanCancel = busy && operation is RegionOperationPhase.PreparingCurrentRegion or RegionOperationPhase.BuildingBackup,
            CanValidate = !isOperationallyBusy && state == RegionPreparationState.Ready,
            CanRestart = !isOperationallyBusy && state is RegionPreparationState.Ready or RegionPreparationState.Outdated,
            CanRestore = !isOperationallyBusy && state == RegionPreparationState.Mixed,
            CanRetry = !isOperationallyBusy && state == RegionPreparationState.Error,
            ShowTopRegionActions = state is RegionPreparationState.Ready or RegionPreparationState.Mixed,
            ShowNotPrepared = state == RegionPreparationState.NotPrepared,
            ShowWaiting = state == RegionPreparationState.WaitingForOtherRegion,
            ShowProgress = state is RegionPreparationState.PreparingCurrentRegion or RegionPreparationState.BuildingBackup or
                RegionPreparationState.SwitchingRegion or RegionPreparationState.ValidatingBackup,
            ShowCompletedPreparationSteps = state == RegionPreparationState.BuildingBackup,
            ShowReady = state == RegionPreparationState.Ready,
            ShowOutdated = state == RegionPreparationState.Outdated,
            ShowMixed = state == RegionPreparationState.Mixed,
            ShowError = state == RegionPreparationState.Error,
            ShowAdvanced = state == RegionPreparationState.Ready,
            ShowGamePathRequired = state == RegionPreparationState.NotPrepared && !status.GamePathValid,
            ShowNotice = !string.IsNullOrWhiteSpace(notice),
            ShowSuccessNotice = notice.StartsWith("区服备份完整", StringComparison.Ordinal),
            VisibleActions = actions,
        };
    }

    private static RegionPreparationState ResolveState(
        RegionSnapshotStatus status, RegionOperationPhase operation, bool restartRequested, string error)
    {
        if (operation == RegionOperationPhase.PreparingCurrentRegion) return RegionPreparationState.PreparingCurrentRegion;
        if (operation == RegionOperationPhase.BuildingBackup) return RegionPreparationState.BuildingBackup;
        if (operation == RegionOperationPhase.SwitchingRegion) return RegionPreparationState.SwitchingRegion;
        if (operation == RegionOperationPhase.ValidatingBackup) return RegionPreparationState.ValidatingBackup;
        if (!string.IsNullOrWhiteSpace(error)) return RegionPreparationState.Error;
        if (restartRequested) return RegionPreparationState.NotPrepared;
        if (status.State == RegionBackupState.Preparing) return RegionPreparationState.WaitingForOtherRegion;
        if (status.State == RegionBackupState.Stale) return RegionPreparationState.Outdated;
        if (status.State == RegionBackupState.Ready && status.GenerationCompatibility == GenerationCompatibility.Compatible &&
            status.CurrentRegion is CurrentGameRegion.Mixed or CurrentGameRegion.Unknown) return RegionPreparationState.Mixed;
        if (status.State == RegionBackupState.Ready) return RegionPreparationState.Ready;
        if (status.State is RegionBackupState.Legacy) return RegionPreparationState.Outdated;
        if (status.State is RegionBackupState.Error) return RegionPreparationState.Error;
        return RegionPreparationState.NotPrepared;
    }

    private static (string Step, string Title, string Description) CopyFor(
        RegionPreparationState state, OverwatchRegion? source, OverwatchRegion? target) => state switch
    {
        RegionPreparationState.NotPrepared => ("步骤 1 / 3", "确认当前游戏区服",
            "首次使用需要准备一次国服和国际服文件。整个过程只需要让 Battle.net 完成一次跨区更新。\n\n请先确认 Battle.net 已经完成当前区服的游戏更新，并且“开始游戏”按钮可以正常使用。然后选择当前电脑上的守望先锋属于哪个区服。"),
        RegionPreparationState.PreparingCurrentRegion => ("步骤 1 / 3", $"正在保存当前{RegionName(source)}文件",
            $"正在将当前{RegionName(source)}游戏文件保存到本地临时区域。\n\n这是本地磁盘复制，不会使用网络流量。根据磁盘速度可能需要一些时间。"),
        RegionPreparationState.WaitingForOtherRegion => ("步骤 2 / 3", $"请在 Battle.net 中切换到{RegionName(target)}",
            $"当前{RegionName(source)}文件已经保存完成。\n\n现在请：\n1. 打开 Battle.net\n2. 将《守望先锋》切换到{RegionName(target)}\n3. 等待 Battle.net 完成游戏更新\n4. 确认游戏已经显示“开始游戏”\n5. 回到这里继续"),
        RegionPreparationState.BuildingBackup => ("步骤 3 / 3", "正在建立区服文件备份",
            "正在比较国服和国际服文件，并保存真正不同的文件。\n\n完成后，以后切换国服和国际服时即可直接使用本地文件，通常不再需要 Battle.net 重复下载这些区服差异。"),
        RegionPreparationState.Ready => ("✓ 准备完成", "区服文件已经准备完成",
            "国服和国际服文件都已经准备完成。\n\n以后可以直接在这里切换区服，绑定了区服的账号在切换账号时也会自动切换对应游戏文件。"),
        RegionPreparationState.Outdated => ("需要重新准备", "游戏已经更新",
            "检测到《守望先锋》版本发生变化，当前国服 / 国际服备份已经不适用于新版本。\n\n需要重新准备一次区服文件。\n\n旧备份会保留到新的准备过程成功完成，不会在开始准备时立即删除。"),
        RegionPreparationState.Mixed => ("需要恢复", "当前区服文件状态不完整",
            "游戏文件可能处于一次未完成的区服切换状态。\n\n现有本地备份仍然可用，可以直接恢复："),
        RegionPreparationState.SwitchingRegion => ("正在处理", "正在恢复区服文件",
            "正在使用现有本地备份恢复守望先锋区服文件，请稍候。"),
        RegionPreparationState.ValidatingBackup => ("正在检查", "正在检查备份完整性",
            "正在检查当前区服备份是否可以正常使用。"),
        _ => ("需要处理", "区服文件暂时无法使用",
            "读取或处理区服文件时遇到问题，请重新检查。"),
    };

    private static IReadOnlyList<RegionPreparationAction> ActionsFor(RegionPreparationState state) => state switch
    {
        RegionPreparationState.NotPrepared => new[] { RegionPreparationAction.ChooseChina, RegionPreparationAction.ChooseInternational },
        RegionPreparationState.PreparingCurrentRegion or RegionPreparationState.BuildingBackup => new[] { RegionPreparationAction.Cancel },
        RegionPreparationState.WaitingForOtherRegion => new[] { RegionPreparationAction.ContinueOtherRegion },
        RegionPreparationState.Ready => new[] { RegionPreparationAction.Validate, RegionPreparationAction.Restart },
        RegionPreparationState.Outdated => new[] { RegionPreparationAction.Restart },
        RegionPreparationState.Mixed => new[] { RegionPreparationAction.RestoreChina, RegionPreparationAction.RestoreInternational },
        RegionPreparationState.Error => new[] { RegionPreparationAction.Retry },
        _ => Array.Empty<RegionPreparationAction>(),
    };

    private static string ProgressFallback(RegionPreparationState state, OverwatchRegion? source) => state switch
    {
        RegionPreparationState.PreparingCurrentRegion => $"正在保存当前{RegionName(source)}文件…",
        RegionPreparationState.BuildingBackup => "正在比较国服和国际服文件…",
        RegionPreparationState.SwitchingRegion => "正在恢复区服文件…",
        RegionPreparationState.ValidatingBackup => "正在检查区服备份…",
        _ => "正在准备区服文件…",
    };

    private static string BackupName(bool complete, bool captured) => complete ? "已准备" : captured ? "已保存，等待另一端" : "尚未准备";
    private static string BackupStateName(RegionBackupState state) => state switch
    {
        RegionBackupState.Ready => "可以使用",
        RegionBackupState.Preparing => "正在准备",
        RegionBackupState.Stale => "需要重新准备",
        RegionBackupState.Empty => "尚未准备",
        RegionBackupState.Legacy => "需要重新准备",
        _ => "需要检查",
    };
    private static string CurrentRegionName(CurrentGameRegion region) => region switch
    {
        CurrentGameRegion.China => "国服",
        CurrentGameRegion.International => "国际服",
        CurrentGameRegion.Mixed => "状态不完整",
        _ => "尚未识别",
    };
    private static string RegionName(OverwatchRegion? region) => region == OverwatchRegion.International ? "国际服" : "国服";
    private static string FormatBytes(long bytes) => bytes >= 1024L * 1024 * 1024
        ? $"{bytes / 1024d / 1024 / 1024:0.0} GB"
        : bytes >= 1024L * 1024 ? $"{bytes / 1024d / 1024:0.0} MB" : $"{bytes / 1024d:0.0} KB";
}
