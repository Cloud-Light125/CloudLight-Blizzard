namespace CloudLightBlizzard.Services;

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
        long targetAccountId, DateTime nowUtc, DateTime deadlineUtc, BattleNetLoginEvidence evidence,
        bool targetObservedThisAttempt = false)
    {
        // A cached ID alone is not a login while the client is absent.
        if (clientRunning && targetObservedThisAttempt && activeAccountId == targetAccountId)
            return AccountSwitchVerificationState.LoggedIn;
        if (evidence == BattleNetLoginEvidence.RealAuthExpired) return AccountSwitchVerificationState.LoginRequired;
        if (nowUtc >= deadlineUtc) return AccountSwitchVerificationState.Unconfirmed;
        if (!clientRunning) return AccountSwitchVerificationState.WaitingForBattleNet;
        return AccountSwitchVerificationState.WaitingForLogin;
    }
}
