namespace CloudLightBlizzard.Services;

/// <summary>
/// Keeps the destructive part of an account switch in one fail-fast sequence.
/// Any failed step prevents every later step, especially account restore and client launch.
/// </summary>
public static class AccountSwitchPipeline
{
    public static async Task ExecuteAsync(
        Func<Task> quitBattleNet,
        Func<Task> saveCurrentAccount,
        Func<Task> normalizeGameRegion,
        Func<Task> restoreTargetAccount,
        Func<Task> launchBattleNet)
    {
        await quitBattleNet();
        await saveCurrentAccount();
        await normalizeGameRegion();
        await restoreTargetAccount();
        await launchBattleNet();
    }
}
