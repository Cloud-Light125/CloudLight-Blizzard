namespace CloudLightBlizzard.Services;

/// <summary>
/// Keeps the destructive part of an account switch in one fail-fast sequence.
/// Account backups are read-only here: switching may restore the target backup, but never save either account.
/// Any failed step prevents every later step, especially account restore and client launch.
/// </summary>
public static class AccountSwitchPipeline
{
    public static async Task ExecuteAsync(
        Func<Task> quitBattleNet,
        Func<Task> normalizeGameRegion,
        Func<Task> restoreTargetAccount,
        Func<Task> launchBattleNet)
    {
        await quitBattleNet();
        await normalizeGameRegion();
        await restoreTargetAccount();
        await launchBattleNet();
    }
}
