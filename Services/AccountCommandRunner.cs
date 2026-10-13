using CloudLightBlizzard.Services.OverwatchRegion;

namespace CloudLightBlizzard.Services;

internal sealed record AccountCommandResult(int ExitCode, string Status, string Message);

internal static class AccountCommandRunner
{
    internal static async Task<AccountCommandResult> RunAsync(string[] args)
    {
        if (!TryParse(args, out var id, out var error)) return new(2, "invalid_arguments", error);
        try
        {
            var settings = AppSettings.Load();
            var paths = new BattleNetPaths();
            if (!string.IsNullOrWhiteSpace(settings.ClientExe)) paths.ClientExe = settings.ClientExe;
            var reader = new AccountReader(paths);
            var controller = new BattleNetController(paths);
            var profiles = new AppDataStore(paths);
            var regions = new OverwatchRegionManager(settings.RegionStoragePath);
            var log = new AccountSwitchLog();
            var service = AccountOperationService.Create(paths, settings, profiles, controller, reader, () => regions, log);
            var probe = new BattleNetAuthLogProbe(paths);
            var cursor = probe.CaptureCursor();
            return await ExecuteAsync(args, service, (target, result, deadline) => Task.Run(() =>
                AccountSwitchVerification.Evaluate(controller.IsClientRunning(), reader.ReadActiveAccountId(),
                    target, DateTime.UtcNow, deadline, probe.ReadAppended(cursor),
                    targetObservedThisAttempt: result.PreviousId.HasValue && result.PreviousId != target)));
        }
        catch (Exception ex) { return new(1, "failed", ex.Message); }
    }

    // Shared CLI core: tests inject the same service boundaries as the GUI and
    // an offline authentication probe, without constructing real controllers.
    internal static async Task<AccountCommandResult> ExecuteAsync(string[] args, AccountOperationService service,
        Func<long, AccountSwitchExecution, DateTime, Task<AccountSwitchVerificationState>> verify,
        TimeSpan? verificationTimeout = null)
    {
        if (!TryParse(args, out var id, out var error)) return new(2, "invalid_arguments", error);
        try
        {
            if (id is null)
            {
                await service.AddAccountAsync();
                return new(0, "login_page_requested", "已清除本地账号指针并启动战网，请手动登录；未宣称登录成功。");
            }
            var result = await service.SwitchAsync(id.Value);
            if (result.AlreadyCurrent)
                return new(3, "unconfirmed", "缓存已指向目标账号，未执行恢复；仅凭旧缓存不能确认本次登录。请在 Battle.net 查看。");
            var deadline = DateTime.UtcNow.Add(verificationTimeout ?? TimeSpan.FromSeconds(120));
            while (true)
            {
                var state = await verify(id.Value, result, deadline);
                if (state == AccountSwitchVerificationState.LoggedIn)
                    return new(0, "logged_in", "已确认目标账号；" + result.Warning);
                if (state == AccountSwitchVerificationState.LoginRequired)
                    return new(3, "login_required", "账号恢复已执行，但认证已过期，需要手动登录。" + result.Warning);
                if (state == AccountSwitchVerificationState.Unconfirmed || DateTime.UtcNow >= deadline)
                    return new(3, "unconfirmed", "账号恢复已执行，但截止时间内未确认登录。" + result.Warning);
                await Task.Delay(500);
            }
        }
        catch (Exception ex) { return new(1, "failed", ex.Message); }
    }
    internal static bool TryParse(string[] args, out long? id, out string error)
    {
        id = null;
        error = "用法：--switch <正整数账号ID> 或 --addaccount（不能附带其他参数）。";
        if (args.Length == 1 && args[0] == "--addaccount") { error = ""; return true; }
        if (args.Length != 2 || args[0] != "--switch" || !long.TryParse(args[1], out var value) || value <= 0) return false;
        id = value;
        error = "";
        return true;
    }
}
