using CloudLightBlizzard.Services.OverwatchRegion;

namespace CloudLightBlizzard.ViewModels;

public enum RegionPreparationState
{
    NotPrepared,
    PreparingCurrentRegion,
    WaitingForOtherRegion,
    WaitingForOriginalRegion,
    AnalyzingOtherRegion,
    VerifyingDifferences,
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
    RedoStep1,
    RedoStep2,
    ReturnToStep1,
}

public sealed class RegionPreparationGuide
{
    public RegionPreparationState State { get; init; }
    public string CurrentFileText { get; init; } = "尚未识别";
    public string ChinaBackupText { get; init; } = "尚未准备";
    public string InternationalBackupText { get; init; } = "尚未准备";
    public string BackupStateText { get; init; } = "尚未准备";
    public string BackupModeText { get; init; } = "智能差异备份";
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
    public string WarningSummaryText { get; init; } = "";
    public string WarningDetailsText { get; init; } = "";
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
    public bool CanRedoStep1 { get; init; }
    public bool CanRedoStep2 { get; init; }
    public bool CanReturnToStep1 { get; init; }
    public bool CanUseFullSnapshot { get; init; }

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
    public bool ShowWarningDetails { get; init; }
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
        OverwatchRegion? operationSource = null,
        RegionBackupMode selectedMode = RegionBackupMode.VerifiedDifference)
    {
        var state = ResolveState(status, operation, restartRequested, error);
        var source = status.PendingSourceRegion ?? operationSource;
        var target = status.PendingTargetRegion ?? (operationSource is null ? null :
            operationSource == OverwatchRegion.China ? OverwatchRegion.International : OverwatchRegion.China);
        var targetName = RegionName(target);
        var current = status.CurrentRegion;
        var isOperationallyBusy = operation != RegionOperationPhase.None || busy;
        var backupCanSwitch = status.SwitchEligibility is RegionSwitchEligibility.Normal or RegionSwitchEligibility.BestEffort;
        var preparationMode = status.State is RegionBackupState.Preparing or RegionBackupState.Ready or
            RegionBackupState.Stale ? status.BackupMode : selectedMode;
        if (restartRequested) preparationMode = selectedMode;
        var actions = ActionsFor(state, preparationMode);
        var (step, title, description) = CopyFor(state, source, target, preparationMode);
        if (status.SwitchEligibility == RegionSwitchEligibility.BestEffort && state == RegionPreparationState.Mixed)
        {
            step = "可以宽容恢复";
            title = "当前游戏版本无法确认";
            description = "区服备份可以使用。当前目录可能包含未记录的新文件，或缺少未记录的旧文件。\n\n" +
                          "恢复时只会修改 Active Generation 已知的国服 / 国际服差异文件，其它文件不会参与处理。";
        }
        if (status.SwitchEligibility == RegionSwitchEligibility.BackupUnavailable &&
            status.State == RegionBackupState.Ready && state == RegionPreparationState.Error &&
            string.IsNullOrWhiteSpace(error))
        {
            title = "区服备份不可用";
            description = "Active Generation、Manifest 或区服目标备份不完整，当前不能切换。\n\n原因：" +
                          status.SwitchEligibilityReason;
        }
        if (status.State == RegionBackupState.Legacy && state == RegionPreparationState.Outdated)
        {
            title = "区服文件需要重新准备";
            description = "区服文件功能已经升级，当前备份无法继续使用。请重新准备一次区服文件。\n\n旧备份会保留到新的准备过程成功完成，不会在开始准备时立即删除。";
        }
        if (state == RegionPreparationState.Ready && status.HasWarnings)
        {
            title = "区服文件已经准备完成，但部分文件存在异常";
            description = $"已确认 {status.DifferenceCount:N0} 个区服差异文件，" +
                          $"自动忽略 {status.RejectedCount:N0} 个非稳定变化，" +
                          $"因文件异常跳过 {status.SkippedFileCount:N0} 个。\n\n" +
                          $"{status.SkippedFileCount:N0} 个文件可能存在异常，已自动跳过。其他区服差异文件仍可正常使用。";
        }
        if (state == RegionPreparationState.WaitingForOriginalRegion && status.CandidateCount > 0)
        {
            description = $"发现候选差异：{status.CandidateCount:N0} 个\n" +
                          $"成功保存候选：{status.CandidateBackupSavedCount:N0} 个\n" +
                          $"因文件异常跳过：{status.SkippedFileCount:N0} 个\n\n" +
                          $"请在 Battle.net 中切回{RegionName(source)}并等待更新完成，然后开始最终验证。";
        }
        if (state == RegionPreparationState.Ready && status.BackupFileIssueCount > 0)
        {
            title = "区服备份可用，但部分文件已损坏或缺失";
            description = $"检测到 {status.BackupFileIssueCount:N0} 个备份文件异常。切换时会自动跳过这些文件，" +
                          "并继续处理其他完整的区服差异文件；异常文件不会复制进游戏目录。";
        }
        if (!string.IsNullOrWhiteSpace(error)) description = error;

        return new RegionPreparationGuide
        {
            State = state,
            CurrentFileText = CurrentRegionName(current),
            ChinaBackupText = BackupName(status.ChinaBackupComplete, status.ChinaCaptured),
            InternationalBackupText = BackupName(status.InternationalBackupComplete, status.InternationalCaptured),
            BackupStateText = BackupStateName(status),
            BackupModeText = preparationMode == RegionBackupMode.VerifiedDifference
                ? "智能差异备份" : "完整备份",
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
            FirstStepCompleteText = source is null ? "" : preparationMode == RegionBackupMode.VerifiedDifference
                ? $"✓ 步骤 1　{RegionName(source)}文件状态已记录"
                : $"✓ 步骤 1　{RegionName(source)}文件已保存",
            SecondStepCompleteText = target is null ? "" : $"✓ 步骤 2　{targetName}差异已记录",
            ContinueButtonText = string.IsNullOrWhiteSpace(notice)
                ? state == RegionPreparationState.WaitingForOriginalRegion
                    ? $"我已切回{RegionName(source)}，开始验证"
                    : $"我已完成{targetName}更新"
                : "重新检查",
            ProgressText = progress?.Message ?? ProgressFallback(state, source),
            WarningSummaryText = status.HasWarnings
                ? $"{status.SkippedFileCount:N0} 个文件存在异常，已自动跳过" : "",
            WarningDetailsText = string.Join("\n\n", status.FileIssues.Select(item =>
                $"{item.RelativePath}\n{item.Reason}")),
            ProgressCurrent = progress is { BytesTotal: > 0 } ? progress.BytesCurrent : progress?.Current ?? 0,
            ProgressTotal = Math.Max(1, progress is { BytesTotal: > 0 } ? progress.BytesTotal : progress?.Total ?? 1),
            ProgressIndeterminate = progress is null || progress.BytesTotal <= 0 && progress.Total <= 0,
            SwitchChinaText = current == CurrentGameRegion.China && status.ExactSnapshotMatch ? "当前为国服" :
                current == CurrentGameRegion.International ? "切换到国服" : "恢复到国服",
            SwitchInternationalText = current == CurrentGameRegion.International && status.ExactSnapshotMatch ? "当前为国际服" :
                current == CurrentGameRegion.China ? "切换到国际服" : "恢复到国际服",
            CanSwitchChina = !isOperationallyBusy && status.GamePathValid && backupCanSwitch &&
                             state is RegionPreparationState.Ready or RegionPreparationState.Mixed &&
                             (current != CurrentGameRegion.China || !status.ExactSnapshotMatch),
            CanSwitchInternational = !isOperationallyBusy && status.GamePathValid && backupCanSwitch &&
                                     state is RegionPreparationState.Ready or RegionPreparationState.Mixed &&
                                     (current != CurrentGameRegion.International || !status.ExactSnapshotMatch),
            CanChangePaths = !isOperationallyBusy,
            CanClear = !isOperationallyBusy && (status.State != RegionBackupState.Empty ||
                                                 !string.IsNullOrWhiteSpace(status.ActiveGenerationId)),
            CanChooseCurrentRegion = !isOperationallyBusy && state == RegionPreparationState.NotPrepared && status.GamePathValid,
            CanContinueOtherRegion = !isOperationallyBusy &&
                                     state is RegionPreparationState.WaitingForOtherRegion or
                                         RegionPreparationState.WaitingForOriginalRegion,
            CanCancel = busy && operation is RegionOperationPhase.PreparingCurrentRegion or RegionOperationPhase.BuildingBackup,
            CanValidate = !isOperationallyBusy && backupCanSwitch && state is RegionPreparationState.Ready or RegionPreparationState.Mixed,
            CanRestart = !isOperationallyBusy && state != RegionPreparationState.NotPrepared,
            CanRestore = !isOperationallyBusy && backupCanSwitch && state == RegionPreparationState.Mixed,
            CanRetry = !isOperationallyBusy && state == RegionPreparationState.Error,
            CanRedoStep1 = !isOperationallyBusy && preparationMode == RegionBackupMode.VerifiedDifference &&
                           state == RegionPreparationState.WaitingForOtherRegion,
            CanRedoStep2 = !isOperationallyBusy && preparationMode == RegionBackupMode.VerifiedDifference &&
                           state is RegionPreparationState.WaitingForOriginalRegion or RegionPreparationState.Error &&
                           status.PreparationCheckpoint == RegionPreparationCheckpoint.Step2Ready,
            CanReturnToStep1 = !isOperationallyBusy && preparationMode == RegionBackupMode.VerifiedDifference &&
                               status.State == RegionBackupState.Preparing &&
                               state is RegionPreparationState.WaitingForOriginalRegion or RegionPreparationState.Error,
            CanUseFullSnapshot = !isOperationallyBusy && preparationMode == RegionBackupMode.VerifiedDifference,
            ShowTopRegionActions = state is RegionPreparationState.Ready or RegionPreparationState.Mixed,
            ShowNotPrepared = state == RegionPreparationState.NotPrepared,
            ShowWaiting = state is RegionPreparationState.WaitingForOtherRegion or
                RegionPreparationState.WaitingForOriginalRegion,
            ShowProgress = state is RegionPreparationState.PreparingCurrentRegion or RegionPreparationState.BuildingBackup or
                RegionPreparationState.AnalyzingOtherRegion or RegionPreparationState.VerifyingDifferences or
                RegionPreparationState.SwitchingRegion or RegionPreparationState.ValidatingBackup,
            ShowCompletedPreparationSteps = state is RegionPreparationState.BuildingBackup or
                RegionPreparationState.AnalyzingOtherRegion or RegionPreparationState.VerifyingDifferences,
            ShowReady = state == RegionPreparationState.Ready,
            ShowOutdated = state == RegionPreparationState.Outdated,
            ShowMixed = state == RegionPreparationState.Mixed,
            ShowError = state == RegionPreparationState.Error,
            ShowAdvanced = state == RegionPreparationState.Ready,
            ShowGamePathRequired = state == RegionPreparationState.NotPrepared && !status.GamePathValid,
            ShowNotice = !string.IsNullOrWhiteSpace(notice),
            ShowSuccessNotice = notice.StartsWith("区服备份完整", StringComparison.Ordinal),
            ShowWarningDetails = state == RegionPreparationState.Ready && status.FileIssues.Count > 0,
            VisibleActions = actions,
        };
    }

    private static RegionPreparationState ResolveState(
        RegionSnapshotStatus status, RegionOperationPhase operation, bool restartRequested, string error)
    {
        if (operation == RegionOperationPhase.PreparingCurrentRegion) return RegionPreparationState.PreparingCurrentRegion;
        if (operation == RegionOperationPhase.BuildingBackup)
        {
            if (status.BackupMode == RegionBackupMode.VerifiedDifference)
                return status.PreparationCheckpoint == RegionPreparationCheckpoint.Step2Ready
                    ? RegionPreparationState.VerifyingDifferences
                    : RegionPreparationState.AnalyzingOtherRegion;
            return RegionPreparationState.BuildingBackup;
        }
        if (operation == RegionOperationPhase.SwitchingRegion) return RegionPreparationState.SwitchingRegion;
        if (operation == RegionOperationPhase.ValidatingBackup) return RegionPreparationState.ValidatingBackup;
        if (!string.IsNullOrWhiteSpace(error)) return RegionPreparationState.Error;
        if (restartRequested) return RegionPreparationState.NotPrepared;
        if (status.State == RegionBackupState.Preparing)
            return status.BackupMode == RegionBackupMode.VerifiedDifference &&
                   status.PreparationCheckpoint == RegionPreparationCheckpoint.Step2Ready
                ? RegionPreparationState.WaitingForOriginalRegion
                : RegionPreparationState.WaitingForOtherRegion;
        if (status.State == RegionBackupState.Stale || status.SwitchEligibility == RegionSwitchEligibility.GameUpdated)
            return RegionPreparationState.Outdated;
        if (status.State == RegionBackupState.Ready && status.SwitchEligibility == RegionSwitchEligibility.BackupUnavailable)
            return RegionPreparationState.Error;
        if (status.State == RegionBackupState.Ready && status.SwitchEligibility == RegionSwitchEligibility.BestEffort)
            return RegionPreparationState.Mixed;
        if (status.State == RegionBackupState.Ready && status.SwitchEligibility == RegionSwitchEligibility.Normal &&
            status.CurrentRegion is CurrentGameRegion.Mixed or CurrentGameRegion.Unknown) return RegionPreparationState.Mixed;
        if (status.State == RegionBackupState.Ready) return RegionPreparationState.Ready;
        if (status.State is RegionBackupState.Legacy) return RegionPreparationState.Outdated;
        if (status.State is RegionBackupState.Error) return RegionPreparationState.Error;
        return RegionPreparationState.NotPrepared;
    }

    private static (string Step, string Title, string Description) CopyFor(
        RegionPreparationState state, OverwatchRegion? source, OverwatchRegion? target,
        RegionBackupMode backupMode) => state switch
    {
        RegionPreparationState.NotPrepared => ("步骤 1 / 3", "确认当前游戏区服",
            backupMode == RegionBackupMode.VerifiedDifference
                ? "智能差异备份需要依次记录当前区服、另一区服，再切回当前区服，用于自动确认真正的区服差异文件。\n\n请先确认 Battle.net 已经完成当前区服的游戏更新，然后选择当前电脑上的守望先锋属于哪个区服。"
                : "完整备份会先把当前区服的完整游戏文件保存到临时区域，再记录另一区服。\n\n请先确认 Battle.net 已经完成当前区服的游戏更新，然后选择当前电脑上的守望先锋属于哪个区服。"),
        RegionPreparationState.PreparingCurrentRegion => ("步骤 1 / 3", $"正在保存当前{RegionName(source)}文件",
            backupMode == RegionBackupMode.VerifiedDifference
                ? $"正在记录当前{RegionName(source)}文件的内容状态。此步骤不会复制整个游戏目录。"
                : $"正在将当前{RegionName(source)}游戏文件保存到本地临时区域。\n\n这是本地磁盘复制，不会使用网络流量。根据磁盘速度可能需要一些时间。"),
        RegionPreparationState.WaitingForOtherRegion => ("步骤 2 / 3", $"请在 Battle.net 中切换到{RegionName(target)}",
            backupMode == RegionBackupMode.VerifiedDifference
                ? $"当前{RegionName(source)}文件状态已经记录完成。\n\n请切换到{RegionName(target)}并等待 Battle.net 完成更新。你可以启动游戏进行验证；退出游戏后再回到这里继续。"
                : $"当前{RegionName(source)}完整文件已经保存到临时区域。\n\n请切换到{RegionName(target)}并等待 Battle.net 完成更新，然后回到这里建立完整备份。"),
        RegionPreparationState.WaitingForOriginalRegion => ("步骤 3 / 3", $"请切回最初的{RegionName(source)}",
            $"另一区服文件差异已经记录完成。\n\n请在 Battle.net 中切回{RegionName(source)}并等待更新完成。你可以启动游戏进行验证；退出游戏后再回来完成最终确认。"),
        RegionPreparationState.AnalyzingOtherRegion => ("步骤 2 / 3", "正在分析另一区服文件差异",
            "正在比较两次记录的文件内容，并保存候选区服差异。游戏启动或关闭造成的变化不会在此时直接判定为最终区服差异。"),
        RegionPreparationState.VerifyingDifferences => ("步骤 3 / 3", "正在验证区服差异文件",
            "正在确认候选文件是否已经恢复到最初区服的内容。无法通过往返验证的变化会自动忽略，不会导致整个准备失败。"),
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

    private static IReadOnlyList<RegionPreparationAction> ActionsFor(RegionPreparationState state,
        RegionBackupMode mode) => state switch
    {
        RegionPreparationState.NotPrepared => new[] { RegionPreparationAction.ChooseChina, RegionPreparationAction.ChooseInternational },
        RegionPreparationState.PreparingCurrentRegion or RegionPreparationState.BuildingBackup or
            RegionPreparationState.AnalyzingOtherRegion or RegionPreparationState.VerifyingDifferences => new[] { RegionPreparationAction.Cancel },
        RegionPreparationState.WaitingForOtherRegion when mode == RegionBackupMode.VerifiedDifference => new[] { RegionPreparationAction.ContinueOtherRegion, RegionPreparationAction.RedoStep1, RegionPreparationAction.Restart },
        RegionPreparationState.WaitingForOtherRegion => new[] { RegionPreparationAction.ContinueOtherRegion, RegionPreparationAction.Restart },
        RegionPreparationState.WaitingForOriginalRegion => new[] { RegionPreparationAction.ContinueOtherRegion, RegionPreparationAction.RedoStep2, RegionPreparationAction.ReturnToStep1, RegionPreparationAction.Restart },
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
        RegionPreparationState.AnalyzingOtherRegion => "正在分析另一区服文件差异……",
        RegionPreparationState.VerifyingDifferences => "正在验证区服差异文件……",
        RegionPreparationState.SwitchingRegion => "正在恢复区服文件…",
        RegionPreparationState.ValidatingBackup => "正在检查区服备份…",
        _ => "正在准备区服文件…",
    };

    private static string BackupName(bool complete, bool captured) => complete ? "已准备" : captured ? "已保存，等待另一端" : "尚未准备";
    private static string BackupStateName(RegionSnapshotStatus status) => status.SwitchEligibility switch
    {
        RegionSwitchEligibility.BestEffort => "可以使用（宽容恢复）",
        RegionSwitchEligibility.BackupUnavailable when status.State == RegionBackupState.Ready => "备份不可用",
        RegionSwitchEligibility.GameUpdated => "需要重新准备",
        _ => status.State switch
        {
            RegionBackupState.Ready => status.BackupFileIssueCount > 0 ? "可用（部分备份文件异常）" :
                status.HasWarnings ? "可用（部分文件已跳过）" : "可以使用",
            RegionBackupState.Preparing => "正在准备",
            RegionBackupState.Stale => "需要重新准备",
            RegionBackupState.Empty => "尚未准备",
            RegionBackupState.Legacy => "需要重新准备",
            _ => "需要检查",
        },
    };
    private static string CurrentRegionName(CurrentGameRegion region) => region switch
    {
        CurrentGameRegion.China => "国服",
        CurrentGameRegion.International => "国际服",
        CurrentGameRegion.Mixed => "状态不完整",
        _ => "无法确认",
    };
    private static string RegionName(OverwatchRegion? region) => region == OverwatchRegion.International ? "国际服" : "国服";
    private static string FormatBytes(long bytes) => bytes >= 1024L * 1024 * 1024
        ? $"{bytes / 1024d / 1024 / 1024:0.0} GB"
        : bytes >= 1024L * 1024 ? $"{bytes / 1024d / 1024:0.0} MB" : $"{bytes / 1024d:0.0} KB";
}
