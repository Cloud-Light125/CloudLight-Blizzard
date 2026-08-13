namespace BnetSwitch.Services;

public enum AccountSwitchVerificationState
{
    WaitingForBattleNet,
    WaitingForLogin,
    LoggedIn,
    LoginRequired,
    Unconfirmed,
}

public static class AccountSwitchVerification
{
    public static AccountSwitchVerificationState Evaluate(bool clientRunning, long? activeAccountId,
        long targetAccountId, DateTime nowUtc, DateTime deadlineUtc, BattleNetLoginEvidence evidence)
    {
        if (!clientRunning) return AccountSwitchVerificationState.WaitingForBattleNet;
        if (activeAccountId == targetAccountId) return AccountSwitchVerificationState.LoggedIn;
        if (evidence == BattleNetLoginEvidence.RealAuthExpired) return AccountSwitchVerificationState.LoginRequired;
        if (nowUtc >= deadlineUtc) return AccountSwitchVerificationState.Unconfirmed;
        return AccountSwitchVerificationState.WaitingForLogin;
    }
}
